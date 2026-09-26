# atvbuilder-boot.sh — ajouté par AndroidTvPcIsoBuilder dans /scripts de l'ISO.
#
# L'init de l'initrd des images Android-x86/BlissOS fait "source" de chaque fichier
# de /src/scripts/* (= dossier "scripts" à la racine de l'ISO) avant switch_root ;
# le contenu de l'ISO est alors accessible sous /mnt/$SRC. Selon la génération
# d'init, le système Android est déjà monté ou pas encore (voir la fin du fichier).
#
# Les fichiers ajoutés portent des noms en minuscules en Joliet (vus tels quels par
# un montage normal) et en majuscules en ISO9660 strict, que le pilote iso9660 de
# Linux présente en minuscules (nojoliet) : les chemins sont donc en minuscules ici.
#
# Le système étant en lecture seule, on remplace des fichiers par "mount --bind",
# exactement comme le fait déjà l'init d'origine (pc.xml, fakeboot.xml...).
#
# Ce fichier est SOURCÉ par le shell de l'init : pas de "exit", pas de "set -e",
# toutes les variables sont préfixées atvb_ pour ne rien écraser.

atvb_log()
{
	echo "atvbuilder: $*" >> /tmp/atvbuilder.log
	echo "atvbuilder: $*" > /dev/kmsg 2> /dev/null
}

atvb_src=/mnt/$SRC

# --- Logo animé dès le début du démarrage --------------------------------------
# atvsplash dessine le logo sur le framebuffer (dès qu'il existe) jusqu'au lancement
# de la bootanimation d'Android, puis s'arrête tout seul. Lancé en premier pour que
# le logo apparaisse le plus tôt possible. Il survit au switch_root de l'init.
if [ -f "$atvb_src/bootanim/atvsplash" ] && [ -f "$atvb_src/bootanim/splash.atvs" ]; then
	"$atvb_src/bootanim/atvsplash" "$atvb_src/bootanim/splash.atvs" < /dev/null > /dev/null 2>&1 &
	atvb_log "logo de demarrage lance (pid $!)"
fi

# Tout ce qui suit modifie le système Android : il doit être monté. $1 = dossier
# racine du système monté (celui qui contient "system/").
atvb_apply_system()
{
	atvb_root=$1

	# --- Animation de démarrage personnalisée ---------------------------------
	# Un bind mount ne peut remplacer qu'un fichier existant : sans animation
	# d'origine, on ne peut rien faire (le système reste en lecture seule). On
	# remplace toutes les variantes présentes (clair/sombre, system/product).
	if [ -f "$atvb_src/bootanim/bootanimation.zip" ]; then
		atvb_done=
		for atvb_target in \
			"$atvb_root"/system/product/media/bootanimation.zip \
			"$atvb_root"/system/product/media/bootanimation-dark.zip \
			"$atvb_root"/product/media/bootanimation.zip \
			"$atvb_root"/system/media/bootanimation.zip; do
			if [ -f "$atvb_target" ] &&
				mount --bind "$atvb_src/bootanim/bootanimation.zip" "$atvb_target"; then
				atvb_done=1
				atvb_log "animation de demarrage remplacee ($atvb_target)"
			fi
		done
		[ -z "$atvb_done" ] && atvb_log "aucune bootanimation.zip d'origine a remplacer, animation ignoree"
	fi

	# --- Démarrage direct sur l'accueil, sans assistant de configuration ------
	# L'assistant LineageOS (LineageSetupWizard) est l'application d'accueil tant
	# que la configuration n'est pas terminée ; sur TV, sa première étape cherche
	# une télécommande Bluetooth et ne peut pas être passée sans en appairer une
	# (ro.setupwizard.mode=DISABLED ne change rien). On le masque par un dossier
	# vide : Android ne le voit pas et démarre directement sur le lanceur TV.
	mkdir -p /tmp/atvb_empty
	for atvb_wizard in \
		"$atvb_root"/system/system_ext/priv-app/LineageSetupWizard \
		"$atvb_root"/system_ext/priv-app/LineageSetupWizard \
		"$atvb_root"/system/product/priv-app/LineageSetupWizard \
		"$atvb_root"/system/priv-app/LineageSetupWizard; do
		if [ -d "$atvb_wizard" ] && mount --bind /tmp/atvb_empty "$atvb_wizard"; then
			atvb_log "assistant de configuration masque ($atvb_wizard)"
		fi
	done

	# L'assistant marque normalement l'appareil comme configuré en fin de parcours
	# (sans ça, bouton Accueil et notifications restent bridés). init.sh appelle en
	# fin de démarrage (bootcomplete) le crochet post_bootcomplete, qu'il ne définit
	# pas : on le définit dans une copie d'init.sh présentée à la place de l'original.
	# Filet : si l'assistant est tout de même installé, il est désactivé et on
	# revient à l'accueil.
	atvb_init="$atvb_root"/system/etc/init.sh
	if [ -f "$atvb_init" ] && ! grep -q "post_bootcomplete()" "$atvb_init"; then
		{
			head -n 1 "$atvb_init"
			cat <<-'ATVB_EOF'
			post_bootcomplete()
			{
				settings put global device_provisioned 1
				settings put secure user_setup_complete 1
				settings put secure tv_user_setup_complete 1
				if pm path org.lineageos.setupwizard > /dev/null 2>&1; then
					pm disable-user --user 0 org.lineageos.setupwizard
					am start -a android.intent.action.MAIN -c android.intent.category.HOME
				fi
			}
			ATVB_EOF
			tail -n +2 "$atvb_init"
		} > /tmp/atvb_init.sh
		if grep -q "do_bootcomplete" /tmp/atvb_init.sh; then
			chmod 644 /tmp/atvb_init.sh
			mount --bind /tmp/atvb_init.sh "$atvb_init" &&
				atvb_log "configuration initiale marquee terminee au demarrage (post_bootcomplete)"
		else
			atvb_log "copie d'init.sh incomplete, configuration initiale laissee telle quelle"
		fi
	fi

	# --- Applications à installer ---------------------------------------------
	# init.sh (do_bootcomplete) installe avec "pm install" chaque fichier de
	# /system/etc/user_app/ une fois par build. On présente à la place un dossier
	# en RAM (/tmp est un tmpfs, il survit au switch_root) contenant les APK
	# d'origine + ceux de l'ISO.
	if [ -d "$atvb_src/apps" ]; then
		if [ -d "$atvb_root"/system/etc/user_app ]; then
			mkdir -p /tmp/atvb_user_app
			cp "$atvb_root"/system/etc/user_app/* /tmp/atvb_user_app/ 2> /dev/null
			cp "$atvb_src"/apps/*.apk /tmp/atvb_user_app/ 2> /dev/null
			chmod 644 /tmp/atvb_user_app/*
			if mount --bind /tmp/atvb_user_app "$atvb_root"/system/etc/user_app; then
				atvb_log "applications ajoutees a l'installation automatique : $(ls /tmp/atvb_user_app | tr '\n' ' ')"
			fi
		else
			atvb_log "pas de /system/etc/user_app sur cette base, applications non installees automatiquement"
		fi
	fi
}

# Deux générations d'init :
#   - Android-x86 historique : le système est déjà monté dans /android et c'est le
#     répertoire courant → on applique tout de suite ;
#   - BlissOS 15+ / LineageOS x86 : les scripts sont sourcés AVANT le montage
#     (process_fstab vient après). L'init appelle ensuite "post_detect", un
#     crochet qu'elle ne définit pas elle-même, juste avant switch_root : on le
#     définit pour appliquer les modifications une fois le système monté.
if [ -d system/etc ]; then
	atvb_apply_system .
elif ! type post_detect 2> /dev/null | grep -q function; then
	post_detect()
	{
		atvb_apply_system "${MODE_BASE:-/android}"
	}
	atvb_log "systeme pas encore monte, modifications differees (post_detect)"
else
	atvb_log "systeme pas encore monte et post_detect deja defini, animation et applications ignorees"
fi
