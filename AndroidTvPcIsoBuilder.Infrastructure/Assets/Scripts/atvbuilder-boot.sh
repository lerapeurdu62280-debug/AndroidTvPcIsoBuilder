# atvbuilder-boot.sh — ajouté par AndroidTvPcIsoBuilder dans /scripts de l'ISO.
#
# L'init de l'initrd des images Android-x86/BlissOS fait "source" de chaque fichier
# de /src/scripts/* (= dossier "scripts" à la racine de l'ISO) après avoir monté le
# système Android dans /android, et avant switch_root. À ce moment :
#   - le répertoire courant est /android (system.img monté en lecture seule) ;
#   - le contenu de l'ISO est accessible sous /mnt/$SRC.
#
# L'ISO produite par DiscUtils stocke ses noms en majuscules (APPS, BOOTANIM...) et
# le pilote iso9660 de Linux les présente en minuscules : on utilise donc toujours
# des chemins en minuscules ici (vérifié en montant une ISO générée sous Linux).
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

# --- Animation de démarrage personnalisée -------------------------------------
# Emplacements possibles de l'animation d'origine selon la base système. Un bind
# mount ne peut remplacer qu'un fichier existant : sans animation d'origine, on
# ne peut rien faire (le système reste en lecture seule).
if [ -f "$atvb_src/bootanim/bootanimation.zip" ]; then
	atvb_done=
	for atvb_target in system/product/media/bootanimation.zip system/media/bootanimation.zip; do
		if [ -f "$atvb_target" ]; then
			if mount --bind "$atvb_src/bootanim/bootanimation.zip" "$atvb_target"; then
				atvb_done=1
				atvb_log "animation de demarrage remplacee ($atvb_target)"
			fi
			break
		fi
	done
	[ -z "$atvb_done" ] && atvb_log "aucune bootanimation.zip d'origine a remplacer, animation ignoree"
fi

# --- Applications à installer --------------------------------------------------
# init.sh (do_bootcomplete) installe avec "pm install" chaque fichier de
# /system/etc/user_app/ une fois par build. On présente à la place un dossier en
# RAM contenant les APK d'origine + ceux de l'ISO.
if [ -d "$atvb_src/apps" ]; then
	if [ -d system/etc/user_app ]; then
		mkdir -p /tmp/atvb_user_app
		cp system/etc/user_app/* /tmp/atvb_user_app/ 2> /dev/null
		cp "$atvb_src"/apps/*.apk /tmp/atvb_user_app/ 2> /dev/null
		chmod 644 /tmp/atvb_user_app/*
		if mount --bind /tmp/atvb_user_app system/etc/user_app; then
			atvb_log "applications ajoutees a l'installation automatique : $(ls /tmp/atvb_user_app | tr '\n' ' ')"
		fi
	else
		atvb_log "pas de /system/etc/user_app sur cette base, applications non installees automatiquement"
	fi
fi
