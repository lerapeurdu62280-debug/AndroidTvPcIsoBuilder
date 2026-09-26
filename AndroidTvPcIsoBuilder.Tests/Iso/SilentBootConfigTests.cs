using AndroidTvPcIsoBuilder.Infrastructure.Iso;

namespace AndroidTvPcIsoBuilder.Tests.Iso;

[TestClass]
public class SilentBootConfigTests
{
    /// <summary>grub.cfg de l'ISO lineage-21.0-20260331-UNOFFICIAL-x86_64_tv (GRUB seul, menu BlissOS).</summary>
    public const string LineageOsGrubConfig = """
        insmod part_gpt
        insmod regexp

        set timeout=30
        set gfxpayload=keep

        set TITLE="LineageOS 21.0"
        set KERNEL_ARGS="root=/dev/ram0 androidboot.live=true ROOT=LABEL=LineageOS_20260331 "
        set KERNEL=/kernel
        set INITRD=/initrd.img

        if [ "$RELOAD" != "true" ]; then
        	source /boot/grub/functions.cfg
        fi

        if [ -z "$src" -a -n "$isofile" ]; then
        	set iso="iso-scan/filename=$isofile"
        fi

        add_entry Try
        add_entry Install "INSTALL=install.sfs"

        submenu --class brunch-settings --class forward "Advanced options" {
        	toggle_ffmpeg
        }

        efi_detect
        """;

    [TestMethod]
    public void FindDefaultGrubEntry_MenuBlissOs_UtiliseLesVariablesKernelInitrdEtArguments()
    {
        var entry = SilentBootConfig.FindDefaultGrubEntry(LineageOsGrubConfig.Replace("\r\n", "\n"));

        Assert.IsNotNull(entry);
        Assert.AreEqual("/kernel", entry.KernelPath);
        Assert.AreEqual("/initrd.img", entry.InitrdPath);
        Assert.AreEqual("root=/dev/ram0 androidboot.live=true ROOT=LABEL=LineageOS_20260331 " + SilentBootConfig.SilentKernelArguments, entry.Arguments);
    }

    [TestMethod]
    public void FindDefaultGrubEntry_MenuentryClassique_IgnoreInstallationEtVariablesInconnues()
    {
        const string config = """
            set timeout=10
            set cmdline="SRC= DATA="
            menuentry "Installer" {
              linux ($root)/kernel INSTALL=1 $cmdline
              initrd ($root)/initrd.img
            }
            menuentry "Android TV" --class android {
              linux ($root)/kernel quiet $cmdline "$iso"
              initrd ($root)/initrd.img
            }
            """;

        var entry = SilentBootConfig.FindDefaultGrubEntry(config.Replace("\r\n", "\n"));

        Assert.IsNotNull(entry);
        Assert.AreEqual("/kernel", entry.KernelPath);
        Assert.AreEqual("/initrd.img", entry.InitrdPath);
        Assert.AreEqual("SRC= DATA= " + SilentBootConfig.SilentKernelArguments, entry.Arguments);
    }

    [TestMethod]
    public void FindDefaultGrubEntry_SansNoyau_RetourneNull()
    {
        Assert.IsNull(SilentBootConfig.FindDefaultGrubEntry("set timeout=60\nsource /efi/boot/android.cfg\n"));
    }

    [TestMethod]
    public void FindDefaultIsolinuxEntry_IgnoreLesModulesC32EtRetientLePremierNoyau()
    {
        const string config = """
            default vesamenu.c32
            label Menu
              kernel vesamenu.c32
            label Live
              menu label Google TV
              kernel kernel_6.1
              append quiet initrd=initrd.img SRC=
            label Autre
              kernel /kernel_6.6
              append initrd=/initrd.img
            """;

        var entry = SilentBootConfig.FindDefaultIsolinuxEntry(config.Replace("\r\n", "\n"));

        Assert.IsNotNull(entry);
        Assert.AreEqual("/kernel_6.1", entry.KernelPath);
        Assert.AreEqual("/initrd.img", entry.InitrdPath);
        Assert.AreEqual("SRC= " + SilentBootConfig.SilentKernelArguments, entry.Arguments);
    }

    /// <summary>Extrait de l'ISO ATV14 v2.6 (MRD Team) : "Firmware Info" est la première entrée.</summary>
    [TestMethod]
    public void FindDefaultIsolinuxEntry_RetientLEntreeMenuDefaultEtPasFirmwareInfo()
    {
        const string config = """
            default vesamenu.c32
            label note
            	menu label NOTE: if your BIOS support UEFI, please Enable it
            label fwinfo
            	menu label Firmware Info
            	kernel /kernel
            	append initrd=/initrd.img root=/dev/ram0 quiet BLOCK=0 FWINFO=1 SRC= DATA=
            label livem
            	menu label Android TV ^LIVE
            	kernel /kernel
            	append initrd=/initrd.img root=/dev/ram0 quiet BLOCK=0 SRC= DATA=
            	menu default
            label install
            	kernel /kernel
            	append initrd=/install.img root=/dev/ram0 quiet INSTALL=1 DEBUG= noexec=off
            """;

        var entry = SilentBootConfig.FindDefaultIsolinuxEntry(config.Replace("\r\n", "\n"));

        Assert.IsNotNull(entry);
        Assert.AreEqual("/initrd.img", entry.InitrdPath);
        Assert.AreEqual("root=/dev/ram0 BLOCK=0 SRC= DATA= " + SilentBootConfig.SilentKernelArguments, entry.Arguments);
    }

    [TestMethod]
    public void FindDefaultIsolinuxEntry_SansMenuDefault_EcarteFirmwareInfoEtInstallation()
    {
        const string config = "label fw\n kernel /kernel\n append initrd=/initrd.img FWINFO=1\n"
            + "label inst\n kernel /kernel\n append initrd=/install.img INSTALL=1\n"
            + "label live\n kernel /kernel\n append initrd=/initrd.img SRC=\n";

        Assert.AreEqual("SRC= " + SilentBootConfig.SilentKernelArguments, SilentBootConfig.FindDefaultIsolinuxEntry(config)!.Arguments);
    }

    [TestMethod]
    public void FindDefaultIsolinuxEntry_ConfigurationQuiChargeUneAutre_RetourneNull()
    {
        Assert.IsNull(SilentBootConfig.FindDefaultIsolinuxEntry("DEFAULT loadconfig\nLABEL loadconfig\n  CONFIG /isolinux/syslinux.cfg\n  APPEND /isolinux/\n"));
    }

    [TestMethod]
    public void WithSilentArguments_RemplaceLesValeursExistantesSansDoublon()
    {
        var arguments = SilentBootConfig.WithSilentArguments("quiet loglevel=7 video=LVDS-1:d");

        Assert.AreEqual("video=LVDS-1:d " + SilentBootConfig.SilentKernelArguments, arguments);
    }

    [TestMethod]
    public void BuildIsolinuxConfig_DemarreSansMenuNiDelai()
    {
        var config = SilentBootConfig.BuildIsolinuxConfig(new BootEntry("/kernel", "/initrd.img", "SRC="));

        Assert.IsFalse(config.Contains('\r'));
        StringAssert.Contains(config, "prompt 0\ntimeout 0");
        StringAssert.Contains(config, "append initrd=/initrd.img SRC=");
    }
}
