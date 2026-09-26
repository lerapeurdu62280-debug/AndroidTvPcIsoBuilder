/*
 * atvsplash — logo animé affiché sur le framebuffer Linux pendant le démarrage,
 * en attendant que la bootanimation d'Android prenne le relais.
 *
 * Lancé en arrière-plan par le script /scripts/atvbuilder de l'ISO (depuis l'initrd,
 * avant switch_root). Le binaire est statique : l'initrd n'a pas de bibliothèques.
 *
 * Déroulé :
 *   1. charge l'image du logo (fichier "ATVS" produit par AndroidTvPcIsoBuilder) ;
 *   2. attend qu'un framebuffer apparaisse (/dev/fb0 : efifb/simpledrm en UEFI dès le
 *      départ, sinon celui du pilote graphique DRM une fois chargé) ;
 *   3. passe la console en mode graphique (KD_GRAPHICS) pour que fbcon n'écrive plus
 *      de texte par-dessus, efface l'écran et dessine le logo centré : fondu d'entrée
 *      puis "respiration", identiques à la bootanimation générée ;
 *   4. se rebranche si le framebuffer change (efifb remplacé par le vrai pilote) ;
 *   5. s'arrête dès que l'affichage d'Android démarre (compositeur, SurfaceFlinger ou
 *      bootanimation), en laissant le logo à l'écran (ou au bout de
 *      30 minutes, par sécurité).
 *
 * Format du fichier logo (little-endian) :
 *   "ATVS" | u16 largeur | u16 hauteur | u16 cycle_ms | u16 fondu_ms | largeur*hauteur*3 octets RGB
 *   (logo déjà composé sur fond noir, à pleine luminosité). Bit 15 de fondu_ms = plein écran :
 *   l'image couvre tout l'écran (bords coupés si les proportions diffèrent).
 *
 * Compilation : voir build-atvsplash.sh (gcc -static sous WSL).
 */
#include <dirent.h>
#include <fcntl.h>
#include <linux/fb.h>
#include <linux/kd.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#define FB_DEVICE "/dev/fb0"
#define FRAME_INTERVAL_MS 33
#define CHECK_INTERVAL_MS 500
/* Filet de sécurité seulement : un premier démarrage (optimisation des applications)
 * peut être long sur un PC modeste. L'arrêt normal, c'est le démarrage de l'affichage Android. */
#define MAX_RUNTIME_MS (30 * 60 * 1000)

struct logo {
	int width, height, cycle_ms, fade_ms, fullscreen;
	unsigned char *rgb;
};

struct fb {
	int fd;
	unsigned char *mem;
	size_t len;
	struct fb_var_screeninfo var;
	struct fb_fix_screeninfo fix;
	int bytes_per_pixel;
	/* Zone du logo à l'écran (après mise à l'échelle éventuelle). */
	int dst_x, dst_y, dst_w, dst_h;
	/* Partie de l'image affichée (recadrage du mode plein écran). */
	int src_x, src_y, src_w, src_h;
	unsigned char *row;
};

static void log_kmsg(const char *message)
{
	int fd = open("/dev/kmsg", O_WRONLY);
	if (fd < 0)
		return;
	char line[256];
	int n = snprintf(line, sizeof line, "atvsplash: %s\n", message);
	if (n > 0)
		if (write(fd, line, (size_t)n) < 0) { /* kmsg indisponible : tant pis */ }
	close(fd);
}

static long now_ms(void)
{
	struct timespec ts;
	clock_gettime(CLOCK_MONOTONIC, &ts);
	return ts.tv_sec * 1000L + ts.tv_nsec / 1000000L;
}

static void sleep_ms(long ms)
{
	struct timespec ts = { ms / 1000, (ms % 1000) * 1000000L };
	nanosleep(&ts, NULL);
}

static unsigned read_u16(const unsigned char *p)
{
	return (unsigned)p[0] | ((unsigned)p[1] << 8);
}

static int load_logo(const char *path, struct logo *logo)
{
	FILE *f = fopen(path, "rb");
	if (!f)
		return -1;
	unsigned char header[12];
	if (fread(header, 1, sizeof header, f) != sizeof header || memcmp(header, "ATVS", 4) != 0) {
		fclose(f);
		return -1;
	}
	logo->width = (int)read_u16(header + 4);
	logo->height = (int)read_u16(header + 6);
	logo->cycle_ms = (int)read_u16(header + 8);
	logo->fade_ms = (int)read_u16(header + 10);
	logo->fullscreen = (logo->fade_ms & 0x8000) != 0;
	logo->fade_ms &= 0x7FFF;
	size_t size = (size_t)logo->width * (size_t)logo->height * 3;
	if (logo->width <= 0 || logo->height <= 0 || logo->cycle_ms <= 0) {
		fclose(f);
		return -1;
	}
	logo->rgb = malloc(size);
	if (!logo->rgb || fread(logo->rgb, 1, size, f) != size) {
		fclose(f);
		return -1;
	}
	fclose(f);
	return 0;
}

/* Les framebuffers exotiques (palette, planaire) sont ignorés : on attend le suivant. */
static int fb_supported(const struct fb_var_screeninfo *var, const struct fb_fix_screeninfo *fix)
{
	return fix->type == FB_TYPE_PACKED_PIXELS
		&& (fix->visual == FB_VISUAL_TRUECOLOR || fix->visual == FB_VISUAL_DIRECTCOLOR)
		&& (var->bits_per_pixel == 16 || var->bits_per_pixel == 24 || var->bits_per_pixel == 32);
}

static void fb_close(struct fb *fb)
{
	if (fb->mem && fb->mem != MAP_FAILED)
		munmap(fb->mem, fb->len);
	if (fb->fd >= 0)
		close(fb->fd);
	free(fb->row);
	memset(fb, 0, sizeof *fb);
	fb->fd = -1;
}

static void fb_clear(struct fb *fb)
{
	for (unsigned y = 0; y < fb->var.yres; y++) {
		size_t offset = (size_t)(y + fb->var.yoffset) * fb->fix.line_length
			+ (size_t)fb->var.xoffset * fb->bytes_per_pixel;
		size_t bytes = (size_t)fb->var.xres * fb->bytes_per_pixel;
		if (offset + bytes <= fb->len)
			memset(fb->mem + offset, 0, bytes);
	}
}

static int fb_open(struct fb *fb, const struct logo *logo)
{
	fb->fd = open(FB_DEVICE, O_RDWR | O_CLOEXEC);
	if (fb->fd < 0)
		return -1;
	if (ioctl(fb->fd, FBIOGET_VSCREENINFO, &fb->var) < 0) {
		fb_close(fb);
		return -1;
	}

	/* Afficher ce framebuffer à l'écran. Avec un pilote DRM, le framebuffer de
	 * l'émulation fbdev n'est affiché qu'après un changement de mode, normalement
	 * déclenché par fbcon. Mais en UEFI, fbcon reporte sa prise en main ("Deferring
	 * console take-over") : sans ce forçage, l'écran continue de montrer l'ancien
	 * contenu de la mémoire vidéo et le logo paraît figé. */
	ioctl(fb->fd, FBIOBLANK, FB_BLANK_UNBLANK);
	fb->var.activate = FB_ACTIVATE_NOW | FB_ACTIVATE_FORCE;
	ioctl(fb->fd, FBIOPUT_VSCREENINFO, &fb->var);

	if (ioctl(fb->fd, FBIOGET_FSCREENINFO, &fb->fix) < 0
		|| ioctl(fb->fd, FBIOGET_VSCREENINFO, &fb->var) < 0
		|| !fb_supported(&fb->var, &fb->fix)
		|| fb->var.xres == 0 || fb->var.yres == 0) {
		fb_close(fb);
		return -1;
	}
	fb->bytes_per_pixel = (int)(fb->var.bits_per_pixel / 8);
	fb->len = fb->fix.smem_len;
	fb->mem = mmap(NULL, fb->len, PROT_READ | PROT_WRITE, MAP_SHARED, fb->fd, 0);
	if (fb->mem == MAP_FAILED) {
		fb->mem = NULL;
		fb_close(fb);
		return -1;
	}

	/* Taille réelle du logo (comme la bootanimation, affichée à l'échelle 1), réduite
	 * seulement si l'écran est plus petit que le logo. */
	fb->src_x = 0;
	fb->src_y = 0;
	fb->src_w = logo->width;
	fb->src_h = logo->height;
	if (logo->fullscreen) {
		/* Couvrir tout l'écran : agrandir jusqu'à remplir, couper ce qui dépasse. */
		double sx = (double)fb->var.xres / logo->width, sy = (double)fb->var.yres / logo->height;
		double scale = sx > sy ? sx : sy;
		fb->src_w = (int)(fb->var.xres / scale);
		fb->src_h = (int)(fb->var.yres / scale);
		if (fb->src_w > logo->width) fb->src_w = logo->width;
		if (fb->src_h > logo->height) fb->src_h = logo->height;
		if (fb->src_w < 1) fb->src_w = 1;
		if (fb->src_h < 1) fb->src_h = 1;
		fb->src_x = (logo->width - fb->src_w) / 2;
		fb->src_y = (logo->height - fb->src_h) / 2;
		fb->dst_w = (int)fb->var.xres;
		fb->dst_h = (int)fb->var.yres;
	} else {
		double scale = 1.0;
		double max_w = fb->var.xres * 0.9, max_h = fb->var.yres * 0.9;
		if (logo->width > max_w)
			scale = max_w / logo->width;
		if (logo->height * scale > max_h)
			scale = max_h / logo->height;
		fb->dst_w = (int)(logo->width * scale);
		fb->dst_h = (int)(logo->height * scale);
	}
	if (fb->dst_w < 1) fb->dst_w = 1;
	if (fb->dst_h < 1) fb->dst_h = 1;
	fb->dst_x = ((int)fb->var.xres - fb->dst_w) / 2;
	fb->dst_y = ((int)fb->var.yres - fb->dst_h) / 2;
	fb->row = malloc((size_t)fb->dst_w * fb->bytes_per_pixel);
	if (!fb->row) {
		fb_close(fb);
		return -1;
	}

	fb_clear(fb);

	char message[160];
	snprintf(message, sizeof message, "framebuffer %s %ux%u %ubpp, logo %dx%d",
		fb->fix.id, fb->var.xres, fb->var.yres, fb->var.bits_per_pixel, fb->dst_w, fb->dst_h);
	log_kmsg(message);
	return 0;
}

/* Le framebuffer a-t-il été remplacé (efifb retiré au profit du pilote DRM, changement
 * de mode...) ? On relit ses caractéristiques via un nouveau descripteur. */
static int fb_changed(const struct fb *fb)
{
	int fd = open(FB_DEVICE, O_RDWR | O_CLOEXEC);
	if (fd < 0)
		return 1;
	struct fb_fix_screeninfo fix;
	struct fb_var_screeninfo var;
	int changed = ioctl(fd, FBIOGET_FSCREENINFO, &fix) < 0
		|| ioctl(fd, FBIOGET_VSCREENINFO, &var) < 0
		|| strncmp(fix.id, fb->fix.id, sizeof fix.id) != 0
		|| fix.smem_start != fb->fix.smem_start
		|| fix.smem_len != fb->fix.smem_len
		|| fix.line_length != fb->fix.line_length
		|| var.xres != fb->var.xres || var.yres != fb->var.yres
		|| var.bits_per_pixel != fb->var.bits_per_pixel;
	close(fd);
	return changed;
}

static uint32_t pack_pixel(const struct fb_var_screeninfo *var, unsigned r, unsigned g, unsigned b)
{
	return ((r >> (8 - var->red.length)) << var->red.offset)
		| ((g >> (8 - var->green.length)) << var->green.offset)
		| ((b >> (8 - var->blue.length)) << var->blue.offset);
}

/* brightness : 0..256 */
static void draw_logo(struct fb *fb, const struct logo *logo, unsigned brightness)
{
	int bpp = fb->bytes_per_pixel;
	for (int y = 0; y < fb->dst_h; y++) {
		int screen_y = fb->dst_y + y;
		if (screen_y < 0 || screen_y >= (int)fb->var.yres)
			continue;
		int src_y = fb->src_y + (int)((long)y * fb->src_h / fb->dst_h);
		const unsigned char *src_row = logo->rgb + (size_t)src_y * logo->width * 3;
		unsigned char *out = fb->row;
		for (int x = 0; x < fb->dst_w; x++) {
			int src_x = fb->src_x + (int)((long)x * fb->src_w / fb->dst_w);
			const unsigned char *p = src_row + src_x * 3;
			uint32_t v = pack_pixel(&fb->var, (p[0] * brightness) >> 8, (p[1] * brightness) >> 8, (p[2] * brightness) >> 8);
			out[0] = (unsigned char)v;
			out[1] = (unsigned char)(v >> 8);
			if (bpp >= 3) out[2] = (unsigned char)(v >> 16);
			if (bpp == 4) out[3] = 0;
			out += bpp;
		}
		size_t offset = (size_t)(screen_y + fb->var.yoffset) * fb->fix.line_length
			+ (size_t)(fb->dst_x + (int)fb->var.xoffset) * bpp;
		size_t bytes = (size_t)fb->dst_w * bpp;
		if (fb->dst_x >= 0 && offset + bytes <= fb->len)
			memcpy(fb->mem + offset, fb->row, bytes);
	}
}

/* Même courbe que BootAnimationGenerator : fondu d'entrée (ease-out cubique) puis
 * respiration en cosinus entre 80 % et 100 % de luminosité. */
static unsigned brightness_at(const struct logo *logo, long elapsed_ms)
{
	double b;
	if (elapsed_ms < logo->fade_ms) {
		double p = (double)elapsed_ms / logo->fade_ms;
		b = 1 - pow(1 - p, 3);
	} else {
		double t = (double)((elapsed_ms - logo->fade_ms) % logo->cycle_ms) / logo->cycle_ms;
		double wave = (1 + cos(2 * M_PI * t)) / 2;
		b = 0.8 + 0.2 * wave;
	}
	return (unsigned)(b * 256);
}

/* Le pipeline graphique d'Android démarre-t-il ? On doit lâcher l'écran AVANT que le
 * compositeur (HAL composer, souvent maître DRM) ou SurfaceFlinger ne s'en empare :
 * un changement de mode fbdev forcé par nous en même temps qu'eux bloque l'affichage
 * (constaté avec vmwgfx : écran figé sur une image de la bootanimation). Le logo reste
 * affiché tel quel dans le framebuffer jusqu'à la première image d'Android.
 * Détection sur le nom de l'exécutable (argv[0]) : "comm" est tronqué à 15 caractères. */
static int android_graphics_starting(void)
{
	DIR *dir = opendir("/proc");
	if (!dir)
		return 0;
	int found = 0;
	struct dirent *entry;
	while (!found && (entry = readdir(dir)) != NULL) {
		if (entry->d_name[0] < '0' || entry->d_name[0] > '9')
			continue;
		char path[300], cmdline[256] = { 0 };
		snprintf(path, sizeof path, "/proc/%s/cmdline", entry->d_name);
		int fd = open(path, O_RDONLY);
		if (fd < 0)
			continue;
		ssize_t n = read(fd, cmdline, sizeof cmdline - 1);
		close(fd);
		if (n <= 0)
			continue;
		/* argv[0] s'arrête au premier '\0' ; on garde le nom sans le chemin. */
		const char *name = strrchr(cmdline, '/');
		name = name ? name + 1 : cmdline;
		if (strcmp(name, "bootanimation") == 0
			|| strcmp(name, "surfaceflinger") == 0
			|| strstr(name, "graphics.composer") != NULL
			|| strstr(name, "hwcomposer") != NULL)
			found = 1;
	}
	closedir(dir);
	return found;
}

/* Empêche fbcon de dessiner du texte (et son curseur) par-dessus le logo. */
static void console_graphics_mode(void)
{
	int fd = open("/dev/tty0", O_RDWR | O_CLOEXEC);
	if (fd < 0)
		return;
	ioctl(fd, KDSETMODE, KD_GRAPHICS);
	close(fd);
}

int main(int argc, char **argv)
{
	if (argc < 2) {
		fprintf(stderr, "usage: %s <logo.atvs>\n", argv[0]);
		return 2;
	}

	struct logo logo;
	if (load_logo(argv[1], &logo) != 0) {
		log_kmsg("logo illisible, abandon");
		return 1;
	}
	log_kmsg("demarre, en attente du framebuffer");

	struct fb fb;
	memset(&fb, 0, sizeof fb);
	fb.fd = -1;

	long start = now_ms();
	long animation_start = 0;
	long last_check = 0;

	for (;;) {
		long now = now_ms();
		if (now - start > MAX_RUNTIME_MS) {
			log_kmsg("delai maximal atteint, arret");
			break;
		}

		if (now - last_check >= CHECK_INTERVAL_MS) {
			last_check = now;
			if (android_graphics_starting()) {
				log_kmsg("affichage Android en cours de demarrage, arret");
				break;
			}
			if (fb.fd >= 0 && fb_changed(&fb)) {
				log_kmsg("framebuffer remplace, rebranchement");
				fb_close(&fb);
			}
			if (fb.fd < 0 && fb_open(&fb, &logo) == 0) {
				console_graphics_mode();
				if (animation_start == 0)
					animation_start = now;
			}
		}

		if (fb.fd >= 0)
			draw_logo(&fb, &logo, brightness_at(&logo, now - animation_start));

		sleep_ms(fb.fd >= 0 ? FRAME_INTERVAL_MS : 100);
	}

	fb_close(&fb);
	return 0;
}
