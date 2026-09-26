#!/bin/sh
# Recompile le binaire atvsplash (x86_64, statique) embarqué dans l'application.
# À lancer depuis ce dossier : sh build-atvsplash.sh
#   - avec Zig (Git Bash sous Windows ou Linux, sans WSL) : ZIG=/chemin/vers/zig sh build-atvsplash.sh
#     (poste de dev : D:/Logiciels_SOSINFOLUDO/Outils/zig-x86_64-windows-0.16.0/zig.exe) ;
#   - sinon avec gcc sous Linux/WSL.
# Le binaire produit est une ressource embarquée (voir le .csproj) : le recommiter après modification.
set -e
cd "$(dirname "$0")"
if [ -n "$ZIG" ]; then
	"$ZIG" cc -target x86_64-linux-musl -static -Os -s -Wall -Wextra -o atvsplash atvsplash.c -lm
else
	gcc -static -Os -s -Wall -Wextra -o atvsplash atvsplash.c -lm
fi
file atvsplash 2> /dev/null || true
ls -l atvsplash
