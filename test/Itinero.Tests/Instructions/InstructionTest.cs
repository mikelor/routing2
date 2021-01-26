using System.Collections.Generic;
using System.Linq;
using Itinero.Instructions;
using Itinero.Instructions.Generators;
using Itinero.Instructions.ToText;
using Itinero.Routes;
using Xunit;
using LinearInstructionGenerator = Itinero.Instructions.LinearInstructionGenerator;

namespace Itinero.Tests.Instructions {
    public class InstructionTest {
        [Fact]
        public void GenerateInstructions_AdvancedRoute_EmitsInstructions() {
            var route = new Route {
                Profile = "bicycle.something",
                Shape = new List<(double longitude, double latitude, float? e)> {
                    // Blokstraat
                    (3.2194970548152924, 51.215430955322816, null), // 0
                    (3.218715190887451, 51.216450776477345, null),

                    // Crossing ring
                    (3.218286037445068, 51.21661878438491, null),

                    // Veldmrschlk Fochstr
                    (3.218286037445068, 51.21686071469478, null),
                    // Werfstraat
                    (3.217722773551941, 51.21750249588503, null),
                    (3.2157111167907715, 51.216222284739125, null), // 5
                    (3.215475082397461, 51.21605763557773, null),


                    //  Kruising scheepsdale: 1ste vak
                    (3.215265870094299, 51.215849303141894, null),
                    //  Kruising scheepsdale: 2de vak; Filips de goedelaan (fietsstraat)
                    (3.2152444124221797, 51.2158005800975, null),
                    (3.213919401168823, 51.21336940272057, null),
                    // Karel de stoute, even links-rechts
                    (3.2133668661117554, 51.212247019059234, null) // 10
                },
                ShapeMeta = new List<Route.Meta> {
                    new() {
                        Distance = 0,
                        Shape = 3,
                        Attributes = new[] {("name", "blokstraat"), ("highway", "residential")}
                    },
                    new() {Distance = 150, Shape = 2, Attributes = new[] {("highway", "cycleway")}},
                    new() {
                        Distance = 15,
                        Shape = 4,
                        Attributes = new[] {("name", "Veldmaarschalk Fochstraat"), ("highway", "residential")}
                    },
                    new() {
                        Distance = 200,
                        Shape = 10,
                        Attributes = new[] {
                            ("name", "Werfstraat"), ("cyclestreet", "yes"), ("highway", "residential")
                        }
                    }
                }
            };

            var start = new Route.Stop
                {Coordinate = (3.219408541917801, 51.21541415412617, null), Shape = 0, Distance = 10};
            // estimated m between the pinned start point and the snapped startpoint

            var stop = new Route.Stop {
                Coordinate = (3.2099054753780365, 51.20692456541283, null), Shape = route.Shape.Count - 1, Distance = 15
            };
            // estimated m between the pinned start point and the snapped startpoint


            route.Stops = new List<Route.Stop> {
                start, stop
            };


            var instructionGenerator = new LinearInstructionGenerator(new BaseInstructionGenerator(),
                new StartInstructionGenerator(), new EndInstructionGenerator());
            var instructions = instructionGenerator.GenerateInstructions(route).ToList();
            var toText = new SubstituteText(new[] {
                ("Turn ", false),
                ("TurnDegrees", true)
            });
            var texts = instructions.Select(toText.ToText).ToList();

            Assert.NotEmpty(instructions);
            Assert.Equal("Turn -57", texts[1]);
        }


        [Fact]
        public void Angles_AllOrthoDirections_CorrectAngle() {
            var eflJuli_klaver = (3.2203030586242676, 51.215446076394535, (float?) null);
            var straightN = (3.2203037291765213, 51.21552000156258, (float?) null);
            var straightE = (3.2204418629407883, 51.215446496424235, (float?) null);

            var n = Utils.AngleBetween(eflJuli_klaver, straightN);
            Assert.True(-5 < n && n < 5);
            var s = Utils.AngleBetween(straightN, eflJuli_klaver);
            Assert.True(-180 <= s && s < -175 || 175 < s && s <= 180);

            var e = Utils.AngleBetween(eflJuli_klaver, straightE);
            Assert.True(-95 < e && e < -85);
            var w = Utils.AngleBetween(straightE, eflJuli_klaver);
            Assert.True(85 <= w && w < 95);
        }
    }
}