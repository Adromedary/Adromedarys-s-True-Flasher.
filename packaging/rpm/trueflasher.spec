Name:           trueflasher
Version:        1.0.0
Release:        1%{?dist}
Summary:        Find the real capacity of fake USB flash drives
License:        MIT
URL:            https://github.com/9M2PJU/Adromedarys-s-True-Flasher.
Source0:        %{name}-%{version}.tar.gz

BuildRequires:  dotnet-sdk-8.0
Requires:       util-linux
# sfdisk lives in util-linux on Fedora/RHEL and in fdisk on openSUSE.
# mkfs helpers are optional at runtime: True Flasher prints manual commands if missing.
Recommends:     dosfstools
Recommends:     exfatprogs

ExclusiveArch:  x86_64 aarch64

%description
Terminal tool that measures how much data a USB flash drive really keeps,
then optionally repartitions the fake to its safe size (sfdisk + mkfs).

%prep
%autosetup -n %{name}-%{version}

%build
# aarch64 builders produce bin/linux-arm64/trueflasher, x86_64 produces bin/linux-x64
if [ "%{_arch}" = "aarch64" ]; then
  ./build.sh arm64
else
  ./build.sh
fi

%install
if [ "%{_arch}" = "aarch64" ]; then RID=linux-arm64; else RID=linux-x64; fi
install -Dm755 bin/$RID/trueflasher %{buildroot}%{_bindir}/trueflasher
install -Dm644 packaging/trueflasher.1 %{buildroot}%{_mandir}/man1/trueflasher.1
install -Dm644 packaging/trueflasher.desktop %{buildroot}%{_datadir}/applications/trueflasher.desktop
install -Dm644 assets/icon_256.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/trueflasher.png
install -Dm644 LICENSE %{buildroot}%{_licensedir}/%{name}/LICENSE

%files
%{_bindir}/trueflasher
%{_mandir}/man1/trueflasher.1*
%{_datadir}/applications/trueflasher.desktop
%{_datadir}/icons/hicolor/256x256/apps/trueflasher.png
%{_licensedir}/%{name}/LICENSE

%changelog
* Sat Nov 14 2026 Adromedary - 1.0.0-1
- Initial packaging
