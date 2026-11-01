# The AUR package: macropad-bin

`PKGBUILD` installs the prebuilt Linux release (`MacroPad-<version>-linux-x64.tar.gz` from the GitHub release):

- `/opt/macropad/MacroPad` and the command `macropad`;
- `/opt/macropad/package-managed`, which tells the app that pacman owns it: it never updates itself and says
  "update it with your package manager" when a newer release is out;
- the udev rule `/usr/lib/udev/rules.d/70-macropad.rules`, so the logged-in user can talk to the pad without root;
- the menu entry and icon.

The release workflow (`.github/workflows/release.yml`) sets `pkgver`, fills in the checksum, writes `.SRCINFO` and
pushes to the AUR on every `v*` tag. That needs the repository secret `AUR_SSH_PRIVATE_KEY`; without it the release
still happens and the AUR step only warns.

## The key

The workflow pushes with an SSH key used only for this package. Its public half is on the AUR account (My Account →
SSH Public Key, one key per line, next to any others), its private half only in the repository secret
`AUR_SSH_PRIVATE_KEY`; no copy is kept anywhere else.

To replace it (lost, leaked, or just rotated):

```sh
ssh-keygen -t ed25519 -C "macropad-aur (GitHub Actions, NickMarcha/MacroPad)" -N "" -f macropad_aur
gh secret set AUR_SSH_PRIVATE_KEY -R NickMarcha/MacroPad < macropad_aur
cat macropad_aur.pub      # add to the AUR account, remove the old line there
ssh -i macropad_aur aur@aur.archlinux.org help   # "Commands: ..." means the AUR accepts it
rm macropad_aur macropad_aur.pub
```

Then run the release workflow for the latest tag (Actions → Release → Run workflow), or push the next tag.

## Testing the package

In this folder on an Arch machine (or `docker run -it archlinux`): set `pkgver` to a released version, then

```sh
updpkgsums && makepkg --printsrcinfo > .SRCINFO && makepkg -si && namcap PKGBUILD *.pkg.tar.zst
```

`namcap` says the dependencies "may not be needed": the single-file binary loads them at run time, which it can't
see. They are needed (Avalonia's X11 and text rendering).
