# AUR - trueflasher-git

Live at https://aur.archlinux.org/packages/trueflasher-git (first published
2026-11-14). Tracks `main` (`-git` package). A versioned `trueflasher`
release package can be added after the first `vX.Y.Z` tag; it is the same
PKGBUILD with `source=(...#tag=v$pkgver)` and no `pkgver()`.

## Initial publish (done 2026-11-14, kept for record)

```bash
git clone ssh://aur@aur.archlinux.org/trueflasher-git.git
cp packaging/arch/PKGBUILD trueflasher-git/
cd trueflasher-git
makepkg --printsrcinfo > .SRCINFO
makepkg -si   # local build + install test
git add PKGBUILD .SRCINFO
git commit -m "initial: trueflasher-git 1.0.0"
git push    # ssh key ~/.ssh/aur, host aur.archlinux.org, user aur
```

## Update after each upstream change

No action needed for `-git` users (they build HEAD). Bump only if
`makedepends` or install paths change:

```bash
cd trueflasher-git
cp ../../Adromedarys-s-True-Flasher./packaging/arch/PKGBUILD .
makepkg --printsrcinfo > .SRCINFO
makepkg -si
git commit -am "sync PKGBUILD" && git push
```

Validate with `namcap PKGBUILD` and `namcap trueflasher-git-*.pkg.tar.zst`.
