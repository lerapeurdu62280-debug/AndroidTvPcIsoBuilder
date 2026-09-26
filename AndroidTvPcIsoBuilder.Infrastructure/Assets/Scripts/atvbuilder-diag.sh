#!/system/bin/sh
# atvbuilder-diag.sh — ISO de diagnostic uniquement (AndroidTvProject.DiagnosticMode).
#
# Service d'init « atvdiag » démarré en fin de démarrage (voir atvbuilder-boot.sh), en
# root. Copie l'état du système (affichage, souris, Wi-Fi, journaux) sur une clé USB
# contenant un dossier ATVLOGS à la racine (FAT32 ou exFAT), en plusieurs relevés pendant
# 10 minutes pour que l'utilisateur ait le temps de bouger la souris et d'essayer le Wi-Fi.
# Seules les partitions contenant ce dossier sont utilisées (démontées sinon).

mnt=/data/local/tmp/atvdiag_mnt
mkdir -p $mnt

# Cherche la clé pendant 2 minutes (elle peut être branchée après le démarrage).
out=
tries=0
while [ -z "$out" ] && [ $tries -lt 24 ]; do
	for dev in /dev/block/sd[a-z]* /dev/block/mmcblk[0-9]*p* /dev/block/nvme*p*; do
		[ -b "$dev" ] || continue
		for fs in vfat exfat; do
			if mount -t $fs "$dev" $mnt 2> /dev/null; then
				for name in ATVLOGS atvlogs Atvlogs; do
					[ -d $mnt/$name ] && out=$mnt/$name && break
				done
				[ -n "$out" ] && break 2
				umount $mnt
			fi
		done
	done
	[ -z "$out" ] && tries=$((tries + 1)) && sleep 5
done
[ -z "$out" ] && exit 0

run=$out/$(date +%Y%m%d-%H%M%S)
mkdir -p $run
echo "Diagnostic en cours... ne pas debrancher la cle avant TERMINE.txt" > $run/EN_COURS.txt
sync

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
	echo "== firmware wifi"; ls -l /vendor/firmware /system/lib/firmware /lib/firmware 2> /dev/null | grep -i -e iwlwifi -e total
} > $run/materiel.txt 2>&1
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
echo "Diagnostic termine, vous pouvez eteindre et debrancher la cle." > $run/TERMINE.txt
sync
umount $mnt
