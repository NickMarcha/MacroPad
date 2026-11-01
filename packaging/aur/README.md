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

## Publishing it the first time

On the CachyOS machine:

1. Register at https://aur.archlinux.org and, under My Account, paste an SSH public key. A key just for the AUR:

   ```
   ssh-keygen -t ed25519 -f ~/.ssh/aur -C aur
   cat ~/.ssh/aur.pub
   ```

   and in `~/.ssh/config`:

   ```
   Host aur.archlinux.org
     IdentityFile ~/.ssh/aur
     User aur
   ```

2. Give the workflow the private key: `gh secret set AUR_SSH_PRIVATE_KEY -R NickMarcha/MacroPad < ~/.ssh/aur`.
3. Run the release workflow again for the current tag (Actions → Release → Run workflow, or
   `gh workflow run release.yml -R NickMarcha/MacroPad -f tag=v0.1.0`). Pushing to a package name that does not
   exist yet creates it.

To test the package locally before that: `makepkg -si` in this folder (after `updpkgsums`).
