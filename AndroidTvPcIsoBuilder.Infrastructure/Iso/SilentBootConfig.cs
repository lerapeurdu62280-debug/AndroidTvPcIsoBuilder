namespace AndroidTvPcIsoBuilder.Infrastructure.Iso;

/// <summary>Entrée de démarrage : noyau, initrd et arguments du noyau (sans le initrd=).</summary>
public sealed record BootEntry(string KernelPath, string InitrdPath, string Arguments);

/// <summary>
/// Remplace les menus de démarrage de l'ISO (ISOLINUX en BIOS, GRUB en UEFI) par un
/// démarrage direct et silencieux sur la première entrée du menu d'origine : plus de menu,
/// plus de texte à l'écran, seul le logo animé (atvsplash puis bootanimation) est visible.
/// </summary>
public static class SilentBootConfig
{
    /// <summary>
    /// Arguments ajoutés au noyau pour ne plus rien afficher en mode texte :
    /// <list type="bullet">
    /// <item><c>quiet loglevel=0</c> : aucun message du noyau à l'écran ;</item>
    /// <item><c>vt.global_cursor_default=0</c> : pas de curseur clignotant ;</item>
    /// <item><c>vt.color=0x00</c> : texte noir sur fond noir, ce qui rend invisible ce que
    /// l'initrd écrit sur la console (bannière, "Detecting...") sans la rediriger : la console
    /// reste utilisable, et ajouter <c>vt.color=0x07</c> au démarrage la rend de nouveau lisible.</item>
    /// </list>
    /// </summary>
    public const string SilentKernelArguments = "quiet loglevel=0 vt.global_cursor_default=0 vt.color=0x00";

    private const string Header = "Généré par AndroidTvPcIsoBuilder : démarrage direct sur le logo, sans menu.";

    /// <summary>
    /// Entrée démarrée par défaut par une configuration ISOLINUX/SYSLINUX, dans l'ordre :
    /// <list type="number">
    /// <item>celle marquée <c>menu default</c> ;</item>
    /// <item>celle nommée par la directive <c>default &lt;label&gt;</c> ;</item>
    /// <item>la première qui démarre Android : les entrées d'installation (<c>INSTALL=</c>,
    /// <c>AUTO_INSTALL=</c>) et d'informations (<c>FWINFO=</c>) sont écartées, car certaines
    /// ISO (ATV14 de MRD Team) placent "Firmware Info" en tête du menu.</item>
    /// </list>
    /// Seules les entrées qui démarrent un noyau Linux avec un initrd comptent (les modules
    /// .c32 comme chain.c32 sont ignorés). Null si la configuration n'en contient aucune, par
    /// exemple quand elle se contente de charger un autre fichier (CONFIG ...).
    /// </summary>
    public static BootEntry? FindDefaultIsolinuxEntry(string config)
    {
        var labels = new List<(string Name, string? Kernel, string? Append, bool IsMenuDefault)>();
        string? defaultLabel = null;

        foreach (var rawLine in config.Split('\n'))
        {
            var (keyword, value) = SplitKeyword(rawLine.Trim());

            switch (keyword)
            {
                case "default":
                    defaultLabel = value;
                    break;
                case "label":
                    labels.Add((value, null, null, false));
                    break;
                case "kernel":
                case "linux":
                    if (labels.Count > 0)
                        labels[^1] = labels[^1] with { Kernel = value };
                    break;
                case "append":
                    if (labels.Count > 0)
                        labels[^1] = labels[^1] with { Append = value };
                    break;
                case "menu":
                    if (labels.Count > 0 && value.Trim().Equals("default", StringComparison.OrdinalIgnoreCase))
                        labels[^1] = labels[^1] with { IsMenuDefault = true };
                    break;
            }
        }

        var bootable = labels
            .Select(l => (l.Name, l.IsMenuDefault, Entry: TryBuild(l.Kernel, l.Append)))
            .Where(l => l.Entry is not null)
            .ToList();

        return bootable.FirstOrDefault(l => l.IsMenuDefault).Entry
            ?? bootable.FirstOrDefault(l => l.Name.Equals(defaultLabel, StringComparison.OrdinalIgnoreCase)).Entry
            ?? bootable.FirstOrDefault(l => !IsInstallerOrToolEntry(l.Entry!)).Entry;
    }

    /// <summary>
    /// Entrée démarrée par une configuration GRUB, pour les ISO sans ISOLINUX (LineageOS TV x86,
    /// BlissOS : GRUB démarre aussi le BIOS via eltorito.img). Deux formes sont reconnues :
    /// <list type="number">
    /// <item>celle de BlissOS, où le menu est généré par <c>add_entry</c> à partir des variables
    /// <c>KERNEL</c>, <c>INITRD</c> et <c>KERNEL_ARGS</c> ;</item>
    /// <item>des <c>menuentry</c> classiques : la première dont <c>linux</c> et <c>initrd</c> se
    /// résolvent en chemins fixes et qui n'est pas une entrée d'installation.</item>
    /// </list>
    /// Les variables définies par <c>set</c> sont remplacées ; un argument qui dépend encore d'une
    /// variable inconnue (options du sous-menu, <c>$isofile</c>...) est retiré, car GRUB le
    /// remplacerait par une chaîne vide. Null si aucune entrée n'est trouvée.
    /// </summary>
    public static BootEntry? FindDefaultGrubEntry(string config)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = new List<(string? Kernel, string? Arguments, string? Initrd)>();
        var insideEntry = false;

        foreach (var rawLine in config.Split('\n'))
        {
            var line = rawLine.Trim();
            var (keyword, value) = SplitKeyword(line);

            switch (keyword)
            {
                case "set":
                    var equals = value.IndexOf('=');
                    if (equals > 0 && !insideEntry)
                        variables[value[..equals].Trim()] = Unquote(value[(equals + 1)..].Trim());
                    break;
                case "menuentry":
                    insideEntry = true;
                    candidates.Add((null, null, null));
                    break;
                case "linux":
                case "linuxefi":
                    if (insideEntry)
                    {
                        var separator = value.IndexOfAny([' ', '\t']);
                        candidates[^1] = candidates[^1] with
                        {
                            Kernel = separator < 0 ? value : value[..separator],
                            Arguments = separator < 0 ? string.Empty : value[(separator + 1)..]
                        };
                    }
                    break;
                case "initrd":
                case "initrdefi":
                    if (insideEntry)
                        candidates[^1] = candidates[^1] with { Initrd = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() };
                    break;
            }

            if (line == "}")
                insideEntry = false;
        }

        if (variables.TryGetValue("KERNEL", out var blissKernel) && variables.TryGetValue("INITRD", out var blissInitrd))
        {
            var blissEntry = TryBuildGrub(blissKernel, variables.GetValueOrDefault("KERNEL_ARGS", string.Empty), blissInitrd, variables);
            if (blissEntry is not null)
                return blissEntry;
        }

        return candidates
            .Select(c => TryBuildGrub(c.Kernel, c.Arguments, c.Initrd, variables))
            .FirstOrDefault(e => e is not null && !IsInstallerOrToolEntry(e));
    }

    private static BootEntry? TryBuildGrub(string? kernel, string? arguments, string? initrd, IReadOnlyDictionary<string, string> variables)
    {
        if (kernel is null || initrd is null)
            return null;

        var kernelPath = ResolveGrubPath(kernel, variables);
        var initrdPath = ResolveGrubPath(initrd, variables);
        if (kernelPath is null || initrdPath is null)
            return null;

        var tokens = (arguments ?? string.Empty)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => ExpandGrubVariables(Unquote(t), variables))
            .SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => !t.Contains('$'));

        return new BootEntry(kernelPath, initrdPath, WithSilentArguments(string.Join(' ', tokens)));
    }

    /// <summary>Chemin absolu sur l'ISO, sans préfixe de périphérique "($root)" ; null s'il reste une variable inconnue.</summary>
    private static string? ResolveGrubPath(string path, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = ExpandGrubVariables(Unquote(path).Replace("($root)", string.Empty).Replace("(${root})", string.Empty), variables);
        if (expanded.Contains('$') || expanded.StartsWith('(') || expanded.Length == 0)
            return null;
        return ToAbsolute(expanded);
    }

    /// <summary>Remplace $VAR et ${VAR} ; les variables inconnues sont laissées telles quelles.</summary>
    private static string ExpandGrubVariables(string value, IReadOnlyDictionary<string, string> variables) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\$\{?(\w+)\}?", m =>
            variables.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static string Unquote(string value) => value.Replace("\"", string.Empty).Replace("'", string.Empty);

    private static readonly string[] NonBootArguments = ["INSTALL=", "AUTO_INSTALL=", "FWINFO="];

    private static bool IsInstallerOrToolEntry(BootEntry entry) =>
        entry.Arguments.Split(' ').Any(token => NonBootArguments.Any(p => token.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    public static string BuildIsolinuxConfig(BootEntry entry) =>
        $"""
        # {Header}
        # Maintenir la touche Maj enfoncée au démarrage pour obtenir l'invite "boot:".
        default atv
        prompt 0
        timeout 0

        label atv
          kernel {entry.KernelPath}
          append initrd={entry.InitrdPath} {entry.Arguments}

        """.Replace("\r\n", "\n");

    /// <summary>
    /// Configuration GRUB équivalente (UEFI, et BIOS pour les ISO démarrées par GRUB seul).
    /// En UEFI, <c>gfxpayload=keep</c> conserve le mode graphique du firmware : le noyau
    /// récupère ainsi tout de suite un framebuffer (efifb), sur lequel atvsplash peut dessiner
    /// le logo dès les premières secondes. En BIOS, GRUB est en mode texte : on demande un mode
    /// VESA pour le noyau, comme le faisait le thème graphique du menu d'origine.
    /// </summary>
    public static string BuildGrubConfig(BootEntry entry) =>
        $$"""
        # {{Header}}
        set timeout=0
        set timeout_style=hidden
        insmod all_video
        if [ "${grub_platform}" = "efi" ]; then
          set gfxpayload=keep
        else
          set gfxpayload=1024x768x32,1024x768,auto
        fi
        search --no-floppy --set=root -f {{entry.KernelPath}}
        linux {{entry.KernelPath}} {{entry.Arguments}}
        initrd {{entry.InitrdPath}}
        boot

        """.Replace("\r\n", "\n");

    /// <summary>Ajoute les arguments silencieux qui ne sont pas déjà présents.</summary>
    public static string WithSilentArguments(string arguments)
    {
        var tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var silent in SilentKernelArguments.Split(' '))
        {
            var key = silent.Split('=')[0];
            tokens.RemoveAll(t => t.Split('=')[0] == key);
            tokens.Add(silent);
        }
        return string.Join(' ', tokens);
    }

    private static BootEntry? TryBuild(string? kernel, string? append)
    {
        if (string.IsNullOrWhiteSpace(kernel) || kernel.EndsWith(".c32", StringComparison.OrdinalIgnoreCase))
            return null;

        var tokens = (append ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var initrdToken = tokens.FirstOrDefault(t => t.StartsWith("initrd=", StringComparison.OrdinalIgnoreCase));
        if (initrdToken is null)
            return null;
        tokens.Remove(initrdToken);

        var initrd = initrdToken["initrd=".Length..];
        return new BootEntry(ToAbsolute(kernel), ToAbsolute(initrd), WithSilentArguments(string.Join(' ', tokens)));
    }

    private static string ToAbsolute(string path) => path.StartsWith('/') ? path : "/" + path;

    private static (string Keyword, string Value) SplitKeyword(string line)
    {
        if (line.Length == 0 || line.StartsWith('#'))
            return (string.Empty, string.Empty);

        var separator = line.IndexOfAny([' ', '\t']);
        return separator < 0
            ? (line.ToLowerInvariant(), string.Empty)
            : (line[..separator].ToLowerInvariant(), line[(separator + 1)..].Trim());
    }
}
