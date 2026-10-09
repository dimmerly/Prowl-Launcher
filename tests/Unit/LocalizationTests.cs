using System.Text.Json;
using System.Text.RegularExpressions;

using Prowl.Launcher;
using Prowl.Rosetta;

using Xunit;

namespace Prowl.Launcher.Test;

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;

[Collection("Localization")]
[Trait("Category", "Unit")]
public sealed class LocalizationTests
{
    private static Dictionary<string, string> Catalog(string locale)
    {
        using Stream stream = typeof( Launcher ).Assembly.GetManifestResourceStream($"Prowl.Launcher.Locale.{locale}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    [Fact]
    public void SampleLabelsUseOptionalTranslationsAndReadableFallbacks()
    {
        Loc.Configure(config => config
            .SetFallbackLocale("en")
            .SetLocale("en")
            .AddProvider(new EmbeddedResourceProvider(typeof(Launcher).Assembly, "Prowl.Launcher.Locale")));

        Sample translated = new("HelloProwl");
        Assert.Equal("Hello Prowl", translated.Name);
        Assert.Equal(Catalog("en")["launcher.samples.hello_prowl_description"], translated.Description);
        Sample unknown = new("NewHTTPSample");
        Assert.Equal("New HTTP Sample", unknown.Name);
        Assert.Equal("", unknown.Description);
    }

    [Fact]
    public void AllLocalesHaveMatchingKeysAndPlaceholders()
    {
        Dictionary<string, string> english = Catalog("en");
        Assert.All(english.Keys, key => Assert.Matches(@"^launcher\.[a-z_]+\.[a-z_]+$", key));
        foreach (string locale in LocaleHelper.Codes)
        {
            Dictionary<string, string> catalog = Catalog(locale);
            Assert.Equal(english.Keys.Order(), catalog.Keys.Order());
            foreach ((string key, string value) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(catalog[key]), $"{locale}: {key}");
                Assert.Equal(Placeholders(value), Placeholders(catalog[key]));
            }
        }

        static string[] Placeholders(string value) => Regex.Matches(value, @"\{\{\w+\}\}")
            .Select(match => match.Value).Order().ToArray();
    }

    [Fact]
    public void RemovalToastAndDynamicMessagesTranslateInEveryLocale()
    {
        Loc.Configure(config => config
            .SetFallbackLocale("en")
            .SetLocale("en")
            .AddProvider(new EmbeddedResourceProvider(typeof( Launcher ).Assembly, "Prowl.Launcher.Locale")));
        try
        {
            foreach (string locale in LocaleHelper.Codes)
            {
                Loc.SetLocale(locale);
                Dictionary<string, string> catalog = Catalog(locale);
                Assert.Equal(catalog["launcher.projects.removed"], Loc.Get("launcher.projects.removed"));
                Assert.Equal(catalog["launcher.projects.files_unchanged"], Loc.Get("launcher.projects.files_unchanged"));
                if (locale != "en")
                {
                    Assert.NotEqual("Project removed", Loc.Get("launcher.projects.removed"));
                    Assert.NotEqual("Project files are unchanged.", Loc.Get("launcher.projects.files_unchanged"));
                }

                string installed = Loc.Get("launcher.versions.installing", new
                {
                    version = "v1.2.3"
                });
                Assert.Equal(catalog["launcher.versions.installing"].Replace("{{version}}", "v1.2.3"), installed);
                Assert.Contains("v1.2.3", installed);
                Assert.DoesNotContain("{{", installed);
                string downloaded = Loc.Get("launcher.download.progress", new
                {
                    name = "editor.zip",
                    progress = Loc.Get("launcher.download.transferred", new
                    {
                        received = "21.0", total = "50.0"
                    })
                });
                Assert.Contains("editor.zip", downloaded);
                Assert.Contains("21.0", downloaded);
                Assert.Contains("50.0", downloaded);
                Assert.DoesNotContain("{{", downloaded);
            }
        }
        finally
        {
            Loc.SetLocale("en");
        }
    }
}
