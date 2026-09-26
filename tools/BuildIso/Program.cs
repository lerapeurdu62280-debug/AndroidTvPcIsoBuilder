using AndroidTvPcIsoBuilder.Domain.Entities;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Infrastructure.BootAnimation;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;
var project = new AndroidTvProject { Name = "Test LineageOS", OutputIsoPath = args[1] };
var builder = new IsoBuilder(new BootAnimationGenerator());
await builder.BuildAsync(project, args[0], new Progress<BuildProgress>(p => Console.WriteLine($"{p.PercentComplete}% {p.Step}")));
var v = builder.Verify(project);
Console.WriteLine($"lisible={v.IsReadable} boot={v.HasBootImage} issues={string.Join(" | ", v.Issues)}");

