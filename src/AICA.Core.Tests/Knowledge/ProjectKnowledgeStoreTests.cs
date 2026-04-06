using System;
using System.Collections.Generic;
using AICA.Core.Knowledge;
using Xunit;

namespace AICA.Core.Tests.Knowledge
{
    public class ProjectKnowledgeStoreTests
    {
        [Fact]
        public void Instance_IsSingleton()
        {
            var a = ProjectKnowledgeStore.Instance;
            var b = ProjectKnowledgeStore.Instance;

            Assert.Same(a, b);
        }

        [Fact]
        public void HasIndex_InitiallyFalse()
        {
            // Clear any previous state
            ProjectKnowledgeStore.Instance.Clear();

            Assert.False(ProjectKnowledgeStore.Instance.HasIndex);
        }

        [Fact]
        public void SetIndex_GetIndex_Roundtrip()
        {
            var index = new ProjectIndex(
                new List<SymbolRecord>(), DateTime.UtcNow, 10, TimeSpan.FromSeconds(1));

            ProjectKnowledgeStore.Instance.SetIndex(index);

            Assert.True(ProjectKnowledgeStore.Instance.HasIndex);
            Assert.Same(index, ProjectKnowledgeStore.Instance.GetIndex());

            // Cleanup
            ProjectKnowledgeStore.Instance.Clear();
        }

        [Fact]
        public void SetIndex_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => ProjectKnowledgeStore.Instance.SetIndex(null));
        }

        [Fact]
        public void Clear_RemovesIndex()
        {
            var index = new ProjectIndex(
                new List<SymbolRecord>(), DateTime.UtcNow, 5, TimeSpan.FromSeconds(0.5));

            ProjectKnowledgeStore.Instance.SetIndex(index);
            Assert.True(ProjectKnowledgeStore.Instance.HasIndex);

            ProjectKnowledgeStore.Instance.Clear();
            Assert.False(ProjectKnowledgeStore.Instance.HasIndex);
            Assert.Null(ProjectKnowledgeStore.Instance.GetIndex());
        }

        [Fact]
        public void CreateProvider_WithIndex_ReturnsProvider()
        {
            var symbols = new List<SymbolRecord>
            {
                new SymbolRecord("test:Foo", "Foo", SymbolKind.Class, "test.h", "NS",
                    "class Foo", new List<string> { "foo" })
            };
            var index = new ProjectIndex(symbols, DateTime.UtcNow, 1, TimeSpan.FromSeconds(0.1));

            ProjectKnowledgeStore.Instance.SetIndex(index);
            var provider = ProjectKnowledgeStore.Instance.CreateProvider();

            Assert.NotNull(provider);

            // Cleanup
            ProjectKnowledgeStore.Instance.Clear();
        }

        [Fact]
        public void CreateProvider_WithoutIndex_ReturnsNull()
        {
            ProjectKnowledgeStore.Instance.Clear();

            Assert.Null(ProjectKnowledgeStore.Instance.CreateProvider());
        }
    }
}
