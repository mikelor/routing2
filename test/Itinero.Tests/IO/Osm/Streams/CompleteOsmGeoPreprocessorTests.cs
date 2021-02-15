using System.Linq;
using Itinero.IO.Osm.Streams;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Streams;
using OsmSharp.Tags;
using Xunit;

namespace Itinero.Tests.IO.Osm.Streams
{
    public class CompleteOsmGeoPreprocessorTests
    {
        [Fact] 
        public void CompleteOsmGeoPreprocessor_NoRelevant_ShouldEnumerateAll()
        {
            var os = new OsmGeo[] {
                new Node() {
                    Id = 0
                },
                new Node() {
                    Id = 1
                },
                new Way() {
                    Id = 2,
                    Nodes = new []{ 0L, 1 }
                }
            };

            var completeStream = new CompleteOsmGeoPreprocessor( _ => false,
                (c, o) => {
                    Assert.True(false);
                });
            completeStream.RegisterSource(os);

            var result = completeStream.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0]?.Id);
            Assert.Equal(1, result[1]?.Id);
            Assert.Equal(2, result[2]?.Id);
        }

        [Fact]
        public void CompleteOsmGeoPreprocessor_CompleteWay_ShouldCallbackOnSecondPass()
        {
            var os = new OsmGeo[] {
                new Node() {
                    Id = 0
                },
                new Node() {
                    Id = 1
                },
                new Way() {
                    Id = 2,
                    Nodes = new []{ 0L, 1 }
                }
            };

            var pass = 0;
            var completeStream = new CompleteOsmGeoPreprocessor( _ => true,
                (c, o) => {
                    Assert.Equal(1, pass);
                    Assert.Equal(2, o.Id);
                    Assert.Equal(2, c.Id);
                    Assert.IsType<Way>(o);
                    Assert.IsType<CompleteWay>(c);
                });
            completeStream.RegisterSource(os);

            var result = completeStream.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0]?.Id);
            Assert.Equal(1, result[1]?.Id);
            Assert.Equal(2, result[2]?.Id);

            pass++;
            result = completeStream.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0]?.Id);
            Assert.Equal(1, result[1]?.Id);
            Assert.Equal(2, result[2]?.Id);
        }

        [Fact]
        public void CompleteOsmGeoPreprocessor_CompleteWay_ReflectChangesOnSecondPass()
        {
            var os = new OsmGeo[] {
                new Node() {
                    Id = 0
                },
                new Node() {
                    Id = 1
                },
                new Way() {
                    Id = 2,
                    Nodes = new []{ 0L, 1 }
                }
            };

            var completeStream = new CompleteOsmGeoPreprocessor( _ => true,
                (c, o) => {
                    o.Tags = new TagsCollection(new Tag("action", "taken"));
                });
            completeStream.RegisterSource(os);

            var result = completeStream.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0]?.Id);
            Assert.Equal(1, result[1]?.Id);
            Assert.Equal(2, result[2]?.Id);

            result = completeStream.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(0, result[0]?.Id);
            Assert.Equal(1, result[1]?.Id);
            Assert.Equal(2, result[2]?.Id);
            Assert.NotNull(result[2].Tags);
            Assert.Equal(new Tag[] { new ("action", "taken") }, result[2].Tags);
        }
    }
}