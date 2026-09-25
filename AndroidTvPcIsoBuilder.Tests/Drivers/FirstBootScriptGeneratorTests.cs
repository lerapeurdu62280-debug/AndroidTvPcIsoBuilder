using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Infrastructure.Drivers;

namespace AndroidTvPcIsoBuilder.Tests.Drivers;

[TestClass]
public class FirstBootScriptGeneratorTests
{
    [TestMethod]
    public void Generate_AucunVendorSelectionne_ProduitUneListeVide()
    {
        var generator = new FirstBootScriptGenerator();
        var selection = new WifiBluetoothDriverSelection { SelectedChipsetVendorIds = new List<string>() };

        var script = generator.Generate(selection);

        StringAssert.Contains(script, "SELECTED_VENDORS=()");
    }

    [TestMethod]
    public void Generate_UnVendorSelectionne_ProduitLaListeAvecCeVendor()
    {
        var generator = new FirstBootScriptGenerator();
        var selection = new WifiBluetoothDriverSelection { SelectedChipsetVendorIds = new List<string> { "realtek" } };

        var script = generator.Generate(selection);

        StringAssert.Contains(script, "SELECTED_VENDORS=(\"realtek\")");
    }

    [TestMethod]
    public void Generate_PlusieursVendorsSelectionnes_ProduitLaListeComplete()
    {
        var generator = new FirstBootScriptGenerator();
        var selection = new WifiBluetoothDriverSelection
        {
            SelectedChipsetVendorIds = new List<string> { "realtek", "broadcom", "intel" }
        };

        var script = generator.Generate(selection);

        StringAssert.Contains(script, "SELECTED_VENDORS=(\"realtek\" \"broadcom\" \"intel\")");
    }

    [TestMethod]
    public void Generate_NeLaisseAucunTokenNonSubstitue()
    {
        var generator = new FirstBootScriptGenerator();
        var selection = new WifiBluetoothDriverSelection { SelectedChipsetVendorIds = new List<string> { "atheros_qualcomm" } };

        var script = generator.Generate(selection);

        StringAssert.DoesNotMatch(script, new System.Text.RegularExpressions.Regex(@"\{\{SELECTED_VENDORS\}\}"));
    }

    [TestMethod]
    public void Generate_ContientLaLogiqueDeDetectionLsusbEtLspci()
    {
        var generator = new FirstBootScriptGenerator();
        var selection = new WifiBluetoothDriverSelection();

        var script = generator.Generate(selection);

        StringAssert.Contains(script, "lsusb");
        StringAssert.Contains(script, "lspci");
        StringAssert.Contains(script, "modprobe");
    }
}
