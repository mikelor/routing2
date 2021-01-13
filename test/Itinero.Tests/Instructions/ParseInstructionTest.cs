using System.Linq;
using Itinero.Instructions.Instructions;
using Itinero.Instructions.ToText;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Itinero.Tests.Instructions {
    public class ParseInstructionTest {
        [Fact]
        public void ParseRenderValue_SimpleValue_SubstitutionInstruction() {
            var parsed = FromJson.ParseRenderValue("Turn $turnDegrees around, so you are at $tUrnDeGReEs");
            var result = parsed.ToText(new BaseInstruction(null, 0, 42));
            Assert.Equal("Turn 42 around, so you are at 42", result);
        }


        [Fact]
        public void ParseRenderValue_SubstitutionWithNoSpace_SubstitutionInstruction() {
            var parsed = FromJson.ParseRenderValue("Take the ${exitNumber}th exit");
            var result = parsed.ToText(new RoundaboutInstruction(null, 0, 5, 42, 5));
            Assert.Equal("Take the 5th exit", result);
        }

        [Fact]
        public void ParseCondition_AdvancedCondition_CorrectResult() {
            var input =
                "{\"start\": { \"$startDegrees>=-45&$startDegrees<=45\": \"Start north\"," +
                " \"$startDegrees>45&$startDegrees<=135\": \"Start east\"," +
                " \"$startDegrees>135&$startDegrees<=225\": \"Start south\", " +
                "\"$startDegrees>225\": \"Start west\" }}";

            var toText = FromJson.ParseInstructionToText(JObject.Parse(input));
            var north = toText.ToText(new StartInstruction(null, 0, 0, 0));
            Assert.Equal("Start north", north);
            var east = toText.ToText(new StartInstruction(null, 0, 50, 0));
            Assert.Equal("Start east", east);
            var west = toText.ToText(new StartInstruction(null, 0, 275, 0));
            Assert.Equal("Start west", west);
        }

        [Fact]
        public void ParseCondition_TypeCondition_BasicCondition() {
            var (p, prior) = FromJson.ParseCondition("base");
            Assert.False(prior);
            Assert.True(p(new BaseInstruction(null, 0, 42)));
            Assert.True(p(new BaseInstruction(null, 0, 43)));
        }

        [Fact]
        public void ParseCondition_EqCondition_BasicCondition() {
            var (p, prior) = FromJson.ParseCondition("$turndegrees=42");
            Assert.False(prior);
            Assert.True(p(new BaseInstruction(null, 0, 42)));
            Assert.False(p(new BaseInstruction(null, 0, 43)));
        }

        [Fact]
        public void ParseCondition_CompareCondition_BasicCondition() {
            var (p, _) = FromJson.ParseCondition("$turndegrees<=42");
            Assert.True(p(new BaseInstruction(null, 0, 35)));
            Assert.True(p(new BaseInstruction(null, 0, 42)));
            Assert.False(p(new BaseInstruction(null, 0, 43)));
            Assert.False(p(new BaseInstruction(null, 0, 50)));

            (p, _) = FromJson.ParseCondition("$turndegrees<42");
            Assert.True(p(new BaseInstruction(null, 0, 35)));
            Assert.False(p(new BaseInstruction(null, 0, 42)));
            Assert.False(p(new BaseInstruction(null, 0, 43)));
            Assert.False(p(new BaseInstruction(null, 0, 50)));


            (p, _) = FromJson.ParseCondition("$turndegrees>42");
            Assert.False(p(new BaseInstruction(null, 0, 35)));
            Assert.False(p(new BaseInstruction(null, 0, 42)));
            Assert.True(p(new BaseInstruction(null, 0, 43)));
            Assert.True(p(new BaseInstruction(null, 0, 50)));

            (p, _) = FromJson.ParseCondition("$turndegrees>=42");
            Assert.False(p(new BaseInstruction(null, 0, 35)));
            Assert.True(p(new BaseInstruction(null, 0, 42)));
            Assert.True(p(new BaseInstruction(null, 0, 43)));
            Assert.True(p(new BaseInstruction(null, 0, 50)));
        }

        [Fact]
        public void ParseCondition_FallbackCondition_BasicCondition() {
            var (p, prior) = FromJson.ParseCondition("*");
            Assert.True(prior);
            Assert.True(p(new BaseInstruction(null, 0, 42)));
            Assert.True(p(new BaseInstruction(null, 0, 43)));
        }

        [Fact]
        public void ParseInstruction_AttemptToGiveName_GivesFallback() {
            var baseInstructionToLeftRight =
                "\"$.name\": {\"$turnDegrees<=-135\": \"Turn sharply left onto $.name\"," +
                "\"$turnDegrees>-135&$turnDegrees<=-65\": \"Turn left onto $.name\",\"$turnDegrees>-65&$turnDegrees<=-25\": \"Turn slightly left onto $.name\",\"$turnDegrees<-25&$turnDegrees<25\": \"Continue onto $.name\",\"$turnDegrees>=135\": \"Turn sharply right onto $.name\",\"$turnDegrees<135&$turnDegrees>=65\": \"Turn right onto $.name\",\"$turnDegrees<65&$turnDegrees>=25\": \"Turn slightly right onto $.name\"}," +
                "\"*\": \"Fallback: $type $turndegrees\"";
            var toText = (ConditionalToText)
                FromJson.ParseInstructionToText(JObject.Parse("{" + baseInstructionToLeftRight + "}"));
            Assert.Equal(2, toText._options.Count());
            Assert.False(toText._options[0].predicate(new StartInstruction(null, 0, 0, 0)));
            Assert.True(toText._options[1].predicate(new StartInstruction(null, 0, 0, 0)));

            var result = toText.ToText(new StartInstruction(null, 0, 20, 0));
            Assert.NotNull(result);
            Assert.Equal("Fallback: start 0", result);
            var left = toText.ToText(new StartInstruction(null, 45, 20, 0));
            Assert.Equal("Fallback: start 45", left);
        }
        
        [Fact]
        public void ParseInstruction_InstructionGoesLeft_GivesLeft() {
            var baseInstructionToLeftRight =
                "\"$turnDegrees<=-135\": \"Turn sharply left\"," +
                "\"$turnDegrees>-135&$turnDegrees<=-65\": \"Turn left\"," +
                "\"$turnDegrees>-65&$turnDegrees<=-25\": \"Turn slightly left\"," +
                "\"$turnDegrees>-25&$turnDegrees<25\": \"Continue\"," +
                "\"$turnDegrees>=135\": \"Turn sharply right\",\"" +
                "$turnDegrees<135&$turnDegrees>=65\": \"Turn right\",\"$turnDegrees<65&$turnDegrees>=25\": \"Turn slightly right\"," +
                "\"*\": \"Fallback: $type $turndegrees\"";
            var toText = (ConditionalToText)
                FromJson.ParseInstructionToText(JObject.Parse("{" + baseInstructionToLeftRight + "}"));
            Assert.Equal(8, toText._options.Count());
            Assert.False(toText._options[0].predicate(new StartInstruction(null, 0, 0, 0)));
            Assert.True(toText._options[3].predicate(new StartInstruction(null, 0, 0, 0)));

            var result = toText.ToText(new StartInstruction(null, 0, 20, 0));
            Assert.NotNull(result);
            Assert.Equal("Continue", result);
            var sright = toText.ToText(new StartInstruction(null, 45, 20, 0));
            Assert.Equal("Turn slightly right", sright);
            
            var right = toText.ToText(new StartInstruction(null, 70, 20, 0));
            Assert.Equal("Turn right", right);
        }

        [Fact]
        public void ParseInstruction_TypeDiscrimination_RegocnizesStart() {
            var baseInstructionToLeftRight =
                "\"start\":\"START\"," +
                "\"base\":\"BASE\"," +
                "\"*\": \"Fallback: $type $turndegrees\"";

            var toText = (ConditionalToText)
                FromJson.ParseInstructionToText(JObject.Parse("{" + baseInstructionToLeftRight + "}"));
            
            Assert.Equal(3, toText._options.Count());
            
            Assert.True(toText._options[0].predicate(new StartInstruction(null, 0, 0, 0)));
            Assert.False(toText._options[1].predicate(new StartInstruction(null, 0, 0, 0)));
            
            Assert.False(toText._options[0].predicate(new BaseInstruction(null, 0, 0, 0)));
            Assert.True(toText._options[1].predicate(new BaseInstruction(null, 0, 0, 0)));

            var start = toText.ToText(new StartInstruction(null, 0, 20, 0));
            Assert.NotNull(start);
            Assert.Equal("START", start);
            var baseResText = toText.ToText(new BaseInstruction(null, 45, 20, 0));
            Assert.Equal("BASE", baseResText);
            
            var fallback = toText.ToText(new RoundaboutInstruction(null, 45, 20, 0,0));
            Assert.Equal("Fallback: roundabout 0", fallback);

        }

    }
}