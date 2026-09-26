using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Infrastructure.BootAnimation;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;
// --diag (n'importe où) : ISO de diagnostic, relevés sur clé USB avec dossier ATVLOGS.
// --app=<apk> (répétable) : application installée au premier démarrage (ex. Aptoide TV).
var diagnostic = args.Contains("--diag");
var apps = args.Where(a => a.StartsWith("--app=")).Select(a => a["--app=".Length..]).ToList();
args = args.Where(a => a != "--diag" && !a.StartsWith("--app=")).ToArray();
var project = new AndroidTvProject { Name = "Test LineageOS", OutputIsoPath = args[1], DiagnosticMode = diagnostic };
foreach (var apk in apps)
    project.Apps.Add(new AppPackage { Name = Path.GetFileNameWithoutExtension(apk), SourceApkPath = apk });
if (args.Length > 2) project.GoogleServices = new GoogleServicesConfig { Enabled = true, DonorIsoPath = args[2], PlayStoreApkPath = args.Length > 3 ? args[3] : null };
var builder = new IsoBuilder(new BootAnimationGenerator());
await builder.BuildAsync(project, args[0], new Progress<BuildProgress>(p => Console.WriteLine($"{p.PercentComplete}% {p.Step}")));
var v = builder.Verify(project);
Console.WriteLine($"lisible={v.IsReadable} boot={v.HasBootImage} issues={string.Join(" | ", v.Issues)}");

