using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raven.Client.Documents.Indexes;
using Tests.Infrastructure;
using Xunit;

namespace FastTests.Client
{
    public class LocalizedStringTests(ITestOutputHelper output) : RavenTestBase(output)
    {
        private const string HeadingPl = "Nagłówek";
        private const string HeadingEn = "Heading";
        private const string LocalePl = "pl";
        private const string LocaleEn = "en";

        [RavenTheory(RavenTestCategory.ClientApi | RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public async Task QueryAsync_By_Index(Options options)
        {
            using var store = GetDocumentStore(options);

            await new HeadingByLocale().ExecuteAsync(store);

            using (var session = store.OpenAsyncSession())
            {
                // articles/1: two locales
                await session.StoreAsync(new Article
                {
                    Heading = new LocalizedString { { LocalePl, HeadingPl }, { LocaleEn, HeadingEn } }
                }, "articles/1");

                // articles/2: English only
                await session.StoreAsync(new Article
                {
                    Heading = new LocalizedString { { LocaleEn, HeadingEn } }
                }, "articles/2");

                await session.SaveChangesAsync();
            }

            await Indexes.WaitForIndexingAsync(store);

            using (var session = store.OpenAsyncSession())
            {
                // Both articles have an English heading
                var articlesEn = await session.Advanced
                    .AsyncDocumentQuery<HeadingByLocale.Entry, HeadingByLocale>()
                    .WhereEquals(e => e.Locale, LocaleEn)
                    .OfType<Article>()
                    .ToListAsync();

                // Only articles/1 has a Polish heading
                var articlesPl = await session.Advanced
                    .AsyncDocumentQuery<HeadingByLocale.Entry, HeadingByLocale>()
                    .WhereEquals(e => e.Locale, LocalePl)
                    .OfType<Article>()
                    .ToListAsync();

                Assert.Equal(2, articlesEn.Count);
                Assert.Single(articlesPl);

                // Full dictionaries are preserved on loaded documents
                Assert.All(articlesEn, a => Assert.Equal(HeadingEn, a.Heading[LocaleEn]));
                Assert.Equal(HeadingPl, articlesPl[0].Heading[LocalePl]);
            }
        }

        [RavenFact(RavenTestCategory.ClientApi)]
        public void LocalizedString_SingleEntry_ImplicitlyConvertsToString()
        {
            var ls = new LocalizedString { { LocaleEn, HeadingEn } };

            string value = ls;

            Assert.Equal(HeadingEn, value);
        }

        [RavenFact(RavenTestCategory.ClientApi)]
        public void LocalizedStringLocaleConverter_SerializesAsLocaleString()
        {
            var article = new Article
            {
                Heading = new LocalizedString { { LocalePl, HeadingPl }, { LocaleEn, HeadingEn } },
                Title  = new LocalizedString { { LocalePl, "Tytuł" },    { LocaleEn, "Title" } },
                Text   = "content"
            };

            // Returning to client
            var serializer = JsonSerializer.Create();
            serializer.Converters.Add(new LocalizedStringLocaleConverter(LocaleEn));

            var result = JObject.FromObject(article, serializer);

            Assert.Equal(HeadingEn, result[nameof(Article.Heading)].Value<string>());
            Assert.Equal("Title",   result[nameof(Article.Title)].Value<string>());
            Assert.Equal("content", result[nameof(Article.Text)].Value<string>());
        }

        [RavenFact(RavenTestCategory.ClientApi)]
        public void LocalizedStringLocaleConverter_WritesNull_WhenLocaleAbsent()
        {
            var article = new Article
            {
                Heading = new LocalizedString { { LocalePl, HeadingPl } }
            };

            var serializer = JsonSerializer.Create();
            serializer.Converters.Add(new LocalizedStringLocaleConverter(LocaleEn));

            var result = JObject.FromObject(article, serializer);

            Assert.Equal(JTokenType.Null, result[nameof(Article.Heading)].Type);
        }

        public class Article
        {
            public string Id { get; set; }
            public LocalizedString Heading { get; set; }
            public LocalizedString Title { get; set; }
            public string Text { get; set; }
        }

        /// <summary>
        /// Fan-out index that creates one entry per locale found in <see cref="Article.Heading"/>,
        /// enabling queries like: <c>.WhereEquals(e => e.Locale, "en")</c>
        /// </summary>
        public class HeadingByLocale : AbstractIndexCreationTask<Article>
        {
            public class Entry
            {
                public string Locale { get; set; }
            }

            public HeadingByLocale()
            {
                Map = articles =>
                    from article in articles
                    where article.Heading.Values != null
                    from heading in article.Heading
                    select new Entry
                    {
                        Locale = heading.Key,
                    };
            }
        }
    }

    /// <summary>
    /// A localized string stored as a plain locale-keyed dictionary (e.g. <c>{"en":"Heading","pl":"Nagłówek"}</c>).
    /// No custom converter is needed for document storage — the dictionary serializes naturally as a JSON object.
    /// Use <see cref="LocalizedStringLocaleConverter"/> to collapse to a single string when responding to clients.
    /// When exactly one locale is present, <see cref="op_Implicit"/> returns that value directly.
    /// </summary>
    public sealed class LocalizedString : Dictionary<string, string>
    {
        /// <summary>
        /// Returns the single localized value when exactly one locale is stored; null otherwise.
        /// </summary>
        public static implicit operator string(LocalizedString ls)
        {
            if (ls is null || ls.Count != 1) return null;
            return ls.Values.First();
        }
    }

    /// <summary>
    /// A write-only <see cref="JsonConverter"/> that serializes <see cref="LocalizedString"/> properties
    /// as a plain JSON string for the configured locale, instead of the full dictionary object.
    /// Intended for outgoing HTTP responses scoped to a single locale.
    /// </summary>
    public sealed class LocalizedStringLocaleConverter(string locale) : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(LocalizedString);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var ls = (LocalizedString)value;
            if (ls == null || !ls.TryGetValue(locale, out var text))
            {
                writer.WriteNull();
                return;
            }
            writer.WriteValue(text);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            => throw new NotSupportedException($"{nameof(LocalizedStringLocaleConverter)} is write-only.");
    }
}


