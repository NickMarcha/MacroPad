#!/bin/bash
# Builds and lints the AUR package for a released version in an Arch container, as the release workflow will.
#
#   docker run --rm -v "$PWD:/repo:ro" archlinux:latest bash /repo/tools/linux/aur-check.sh 0.8.2
# (from the repository root; the repository is mounted read-only and nothing is written back)
set -e
version=${1:?usage: aur-check.sh <released version>}
pacman -Syu --noconfirm --needed base-devel namcap pacman-contrib >/dev/null 2>&1
useradd -m builder && cp -r /repo/packaging/aur /home/builder/pkg && chown -R builder /home/builder/pkg && cd /home/builder/pkg
sed -i "s/^pkgver=.*/pkgver=$version/" PKGBUILD
su builder -c "updpkgsums && makepkg --printsrcinfo > .SRCINFO && makepkg -f --nodeps"
echo "== namcap (\"may not be needed\" dependency warnings are expected: Avalonia loads them at run time)"
namcap PKGBUILD *.pkg.tar.zst
echo "== contents"; tar -tf *.pkg.tar.zst | grep -v '/$'
