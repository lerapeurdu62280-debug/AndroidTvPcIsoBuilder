#!/system/bin/sh
# atvbuilder-diag.sh — ISO de diagnostic uniquement (AndroidTvProject.DiagnosticMode).
#
# Service d'init « atvdiag » démarré tôt (déclencheur « boot », voir atvbuilder-boot.sh),
# en root. Copie l'état du système (affichage, souris, Wi-Fi, journaux) sur une clé USB
# contenant un dossier ATVLOGS à la racine (FAT32, exFAT ou NTFS), en plusieurs relevés pendant
# 10 minutes pour que l'utilisateur ait le temps de bouger la souris et d'essayer le Wi-Fi.
# Seules les partitions contenant ce dossier sont utilisées (démontées sinon).
# Le journal est aussi écrit en continu dès le début, pour les machines qu'Android éteint
# avant la fin du démarrage (batterie ou température mal lues sur certains portables).

mnt=/data/local/tmp/atvdiag_mnt
mkdir -p $mnt

# Débogage ADB par le réseau (port 5555) : permet d'analyser en direct depuis un PC du
# réseau local (adb connect <ip>:5555) quand aucune clé n'est lisible (NTFS non géré
# par certains noyaux). Si ro.adb.secure=1, Android affiche une demande d'autorisation.
# Appelé une fois le démarrage terminé (settings exige system_server).
enable_network_adb()
{
	settings put global adb_enabled 1 2> /dev/null
	settings put global development_settings_enabled 1 2> /dev/null
	setprop service.adb.tcp.port 5555
	setprop persist.adb.tcp.port 5555
	stop adbd
	start adbd
}

# Attend la fin du démarrage (5 minutes au plus : on relève même si elle n'arrive jamais).
wait_boot_completed()
{
	w=0
	while [ "$(getprop sys.boot_completed)" != 1 ] && [ $w -lt 150 ]; do
		sleep 2
		w=$((w + 1))
	done
}

# Cherche la clé pendant 2 minutes (elle peut être branchée après le démarrage).
out=
tries=0
while [ -z "$out" ] && [ $tries -lt 24 ]; do
	for dev in /dev/block/sd[a-z]* /dev/block/mmcblk[0-9]*p* /dev/block/nvme*p*; do
		[ -b "$dev" ] || continue
		for fs in vfat exfat ntfs3 ntfs-3g; do
			# ntfs-3g : binaire FUSE fourni par android-x86 quand le noyau n'a pas ntfs3.
			if [ $fs = ntfs-3g ]; then
				command -v ntfs-3g > /dev/null || continue
				ntfs-3g "$dev" $mnt 2> /dev/null || continue
			elif ! mount -t $fs "$dev" $mnt 2> /dev/null; then
				continue
			fi
			for name in ATVLOGS atvlogs Atvlogs; do
				[ -d $mnt/$name ] && out=$mnt/$name && break
			done
			[ -n "$out" ] && break 2
			umount $mnt
		done
	done
	[ -z "$out" ] && tries=$((tries + 1)) && sleep 5
done
if [ -z "$out" ]; then
	wait_boot_completed
	enable_network_adb
	exit 0
fi

run=$out/$(date +%Y%m%d-%H%M%S)
mkdir -p $run
echo "Diagnostic en cours... ne pas debrancher la cle avant TERMINE.txt" > $run/EN_COURS.txt
sync

# Journal continu (logcat, noyau, batterie, températures) + sync toutes les 2 s : si Android
# éteint la machine, la raison (ShutdownThread, BatteryService, thermal...) reste sur la clé.
# Ces processus sont enfants du service : ils vivent tant que ce script tourne (voir fin).
# logcat en flux reste vide après une coupure (taille FAT jamais écrite) : copie complète
# toutes les 3 s en alternant deux fichiers, pour qu'il en reste toujours un entier.
{
	n=0
	while :; do
		logcat -d -b all -v threadtime > $run/logcat-continu-$((n % 2)).txt 2>&1
		sync
		n=$((n + 1))
		sleep 3
	done
} &
cat /proc/kmsg > $run/dmesg-continu.txt 2>&1 &
{
	while :; do
		echo "== $(date) boot_completed=$(getprop sys.boot_completed) shutdown=$(getprop sys.powerctl)"
		for p in /sys/class/power_supply/*; do
			echo "$p type=$(cat $p/type 2> /dev/null) online=$(cat $p/online 2> /dev/null) present=$(cat $p/present 2> /dev/null) capacity=$(cat $p/capacity 2> /dev/null) status=$(cat $p/status 2> /dev/null)"
		done
		for z in /sys/class/thermal/thermal_zone*; do
			echo "$z $(cat $z/type 2> /dev/null) temp=$(cat $z/temp 2> /dev/null)"
		done
		sleep 2
	done
} > $run/batterie-temperature.txt 2>&1 &
while :; do sync; sleep 2; done &

wait_boot_completed
enable_network_adb

# Informations fixes (une seule fois)
cat /proc/cmdline > $run/cmdline.txt 2>&1
getprop > $run/getprop.txt 2>&1
uname -a > $run/uname.txt 2>&1
lsmod > $run/lsmod.txt 2>&1
cat /system/etc/init.sh > $run/init.sh.txt 2>&1
{
	echo "== fb0 driver"; readlink /sys/class/graphics/fb0/device/driver
	echo "== drm"; ls -l /sys/class/drm/
	for c in /sys/class/drm/card*-*; do
		echo "== $c : $(cat $c/status 2> /dev/null) $(cat $c/enabled 2> /dev/null)"
		cat $c/modes 2> /dev/null
	done
	echo "== dri"; ls -l /dev/dri/
	echo "== hw libs"
	ls -l /vendor/lib64/hw /vendor/lib/hw /system/lib64/hw /system/lib/hw
	echo "== vendor init"; ls -l /vendor/etc/init /system/etc/init
	echo "== vintf"; ls -l /vendor/etc/vintf /vendor/etc/vintf/manifest
	echo "== lspci"; for p in /sys/bus/pci/devices/*; do
		echo "$p $(cat $p/vendor) $(cat $p/device) $(cat $p/class) $(readlink $p/driver)"
	done
	echo "== input"; cat /proc/bus/input/devices
	echo "== power_supply"; for p in /sys/class/power_supply/*; do echo "-- $p"; cat $p/uevent; done
	echo "== thermal"; for z in /sys/class/thermal/thermal_zone*; do
		echo "-- $z $(cat $z/type) temp=$(cat $z/temp)"
		for t in $z/trip_point_*_temp; do [ -f $t ] && echo "$t=$(cat $t) $(cat ${t%_temp}_type)"; done
	done
	echo "== dmi"; cat /sys/class/dmi/id/sys_vendor /sys/class/dmi/id/product_name 2> /dev/null
	echo "== firmware wifi"; ls -l /vendor/firmware /system/lib/firmware /lib/firmware 2> /dev/null | grep -i -e iwlwifi -e total
} > $run/materiel.txt 2>&1
{
	echo "== asound cards"; cat /proc/asound/cards
	echo "== asound pcm"; cat /proc/asound/pcm
	for e in /proc/asound/card*/eld* /proc/asound/card*/codec*; do
		[ -f "$e" ] && { echo "== $e"; head -40 "$e"; }
	done
	echo "== audio hal"; ls -l /vendor/lib64/hw /system/lib64/hw 2> /dev/null | grep -i audio
	echo "== audio props"; getprop | grep -i -e audio -e alsa -e hdmi
	for f in /vendor/etc/audio_policy_configuration.xml /vendor/etc/audio/*.xml /system/etc/audio_policy_configuration.xml; do
		[ -f "$f" ] && { echo "==== $f"; cat "$f"; }
	done
} > $run/son.txt 2>&1
{
	echo "== hals"; lshal -i 2> /dev/null | grep -i -e drm -e media -e codec
	ls -l /vendor/lib64/mediadrm /vendor/lib/mediadrm /system/lib64/mediadrm 2> /dev/null
	echo "== drm props"; getprop | grep -i -e drm -e widevine -e codec -e media
	for f in /vendor/etc/media_codecs*.xml /system/etc/media_codecs*.xml /apex/com.android.media.swcodec/etc/*.xml; do
		[ -f "$f" ] && { echo "==== $f"; cat "$f"; }
	done
} > $run/video.txt 2>&1
for f in /vendor/etc/init/*hwcomposer* /vendor/etc/init/*composer* /vendor/etc/init/*gralloc* \
	/vendor/etc/init/*wifi* /vendor/etc/init/*supplicant* /system/etc/init/hw/init.*.rc /vendor/etc/init/hw/*.rc; do
	[ -f "$f" ] && { echo "==== $f"; cat "$f"; }
done > $run/rc.txt 2>&1

mount -t debugfs debugfs /sys/kernel/debug 2> /dev/null

snapshot()
{
	s=$run/releve-$1
	mkdir -p $s
	date > $s/date.txt
	dmesg > $s/dmesg.txt 2>&1
	logcat -d -b all -v threadtime > $s/logcat.txt 2>&1
	dumpsys SurfaceFlinger > $s/surfaceflinger.txt 2>&1
	dumpsys input > $s/input.txt 2>&1
	dumpsys display > $s/display.txt 2>&1
	dumpsys window displays > $s/window.txt 2>&1
	dumpsys wifi > $s/wifi.txt 2>&1
	dumpsys connectivity > $s/connectivity.txt 2>&1
	ps -A -o PID,PPID,USER,NAME,ARGS > $s/ps.txt 2>&1
	top -b -n 1 -m 25 > $s/top.txt 2>&1
	ip addr > $s/ip.txt 2>&1
	dumpsys media.audio_flinger > $s/audioflinger.txt 2>&1
	dumpsys audio > $s/audio.txt 2>&1
	dumpsys media.player > $s/mediaplayer.txt 2>&1
	getprop > $s/getprop.txt 2>&1
	for d in /sys/kernel/debug/dri/*; do
		[ -f $d/state ] && cat $d/state > $s/dri-state-${d##*/}.txt 2>&1
	done
	sync
}

snapshot 1-demarrage
sleep 120; snapshot 2-apres-2min
sleep 180; snapshot 3-apres-5min
sleep 300; snapshot 4-apres-10min

rm -f $run/EN_COURS.txt
echo "Diagnostic termine, eteignez Android TV avant de debrancher la cle." > $run/TERMINE.txt
sync
# Reste en vie : init tuerait le journal continu à la sortie du service.
wait
