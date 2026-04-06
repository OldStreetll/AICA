using System;
using System.Collections.Generic;
using System.Linq;
using AICA.Core.Knowledge;
using Xunit;

namespace AICA.Core.Tests.Knowledge
{
    public class IncrementalIndexTests
    {
        private static SymbolRecord MakeSymbol(string name, string filePath, SymbolKind kind = SymbolKind.Function)
        {
            return new SymbolRecord(
                id: $"{filePath}:{name}:1",
                name: name,
                kind: kind,
                filePath: filePath,
                ns: "",
                summary: $"{kind} {name}",
                keywords: new List<string> { name.ToLowerInvariant() });
        }

        private static ProjectIndex MakeIndex(params SymbolRecord[] symbols)
        {
            return new ProjectIndex(
                symbols: symbols.ToList(),
                indexedAt: DateTime.UtcNow,
                fileCount: symbols.Select(s => s.FilePath).Distinct().Count(),
                indexDuration: TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void UpdateFileSymbols_ReplacesSymbolsForFile()
        {
            var store = ProjectKnowledgeStore.Instance;
            store.Clear();

            var original = MakeIndex(
                MakeSymbol("Foo", "src/a.cpp", SymbolKind.Class),
                MakeSymbol("Bar", "src/b.cpp", SymbolKind.Class));
            store.SetIndex(original);

            var newSymbols = new List<SymbolRecord>
            {
                MakeSymbol("FooRenamed", "src/a.cpp", SymbolKind.Class),
                MakeSymbol("FooHelper", "src/a.cpp", SymbolKind.Function)
            };
            store.UpdateFileSymbols("src/a.cpp", newSymbols);

            var index = store.GetIndex();
            Assert.Equal(3, index.Symbols.Count);
            Assert.Contains(index.Symbols, s => s.Name == "FooRenamed");
            Assert.Contains(index.Symbols, s => s.Name == "FooHelper");
            Assert.Contains(index.Symbols, s => s.Name == "Bar");
            Assert.DoesNotContain(index.Symbols, s => s.Name == "Foo");

            store.Clear();
        }

        [Fact]
        public void UpdateFileSymbols_EmptyNewSymbols_RemovesFileFromIndex()
        {
            var store = ProjectKnowledgeStore.Instance;
            store.Clear();

            var original = MakeIndex(
                MakeSymbol("Foo", "src/a.cpp", SymbolKind.Class),
                MakeSymbol("Bar", "src/b.cpp", SymbolKind.Class));
            store.SetIndex(original);

            store.UpdateFileSymbols("src/a.cpp", new List<SymbolRecord>());

            var index = store.GetIndex();
            Assert.Single(index.Symbols);
            Assert.Equal("Bar", index.Symbols[0].Name);

            store.Clear();
        }

        [Fact]
        public void UpdateFileSymbols_NoIndex_IsNoOp()
        {
            var store = ProjectKnowledgeStore.Instance;
            store.Clear();

            store.UpdateFileSymbols("src/a.cpp", new List<SymbolRecord>
            {
                MakeSymbol("Foo", "src/a.cpp")
            });

            Assert.False(store.HasIndex);

            store.Clear();
        }

        [Fact]
        public void UpdateFileSymbols_InvalidatesProvider()
        {
            var store = ProjectKnowledgeStore.Instance;
            store.Clear();

            var original = MakeIndex(
                MakeSymbol("UniqueSymbolXyz", "src/a.cpp", SymbolKind.Class));
            store.SetIndex(original);

            var providerBefore = store.CreateProvider();
            var contextBefore = providerBefore.RetrieveContext("UniqueSymbolXyz");
            Assert.Contains("UniqueSymbolXyz", contextBefore);

            store.UpdateFileSymbols("src/a.cpp", new List<SymbolRecord>());

            var providerAfter = store.CreateProvider();
            var contextAfter = providerAfter.RetrieveContext("UniqueSymbolXyz");
            Assert.DoesNotContain("UniqueSymbolXyz", contextAfter);

            store.Clear();
        }
    }
}
