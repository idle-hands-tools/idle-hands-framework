using System.Collections.Generic;
using IncrementalFramework.Features.Narrative;

namespace IncrementalFramework.Samples.Expanded
{
    public static partial class ExpandedCampaigns
    {
        public static InvestigationDefinition[] Investigations() => new[]
        {
            new InvestigationDefinition { Id = "station", Name = "Station: interview the porter", Seconds = 150, AvailableUntil = 240, ResultNode = "station-result" },
            new InvestigationDefinition { Id = "market", Name = "Market: meet the clockmaker", Seconds = 150, AvailableUntil = 240, ResultNode = "market-result" },
            new InvestigationDefinition { Id = "archive", Name = "Archive: compare the bell records", Seconds = 180, ResultNode = "archive-result" },
            new InvestigationDefinition { Id = "workshop", Name = "Workshop: inspect the regulator", Seconds = 180, ResultNode = "workshop-result" },
            new InvestigationDefinition { Id = "courier", Name = "Station: follow the late courier", Seconds = 180, AvailableFrom = 240, ResultNode = "courier-result" },
            new InvestigationDefinition { Id = "rooftop", Name = "Market: trace the rooftop signal", Seconds = 120, AvailableFrom = 300, ResultNode = "rooftop-result" },
            new InvestigationDefinition { Id = "vault", Name = "Archive: open the sealed register", Seconds = 180, Requirement = Has("facts", "key"), ResultNode = "vault-result" },
            new InvestigationDefinition { Id = "bell", Name = "Workshop: test a safe shutdown", Seconds = 120, Requirement = Has("facts", "regulator"), ResultNode = "bell-result" }
        };
        public static StoryDefinition Bell()
        {
            var d = new StoryDefinition { Id = "thirteenth-bell-v1", Title = "The Thirteenth Bell", Start = "town-square" };
            d.Variables.AddRange(new[] { V("facts", StoryVariableKind.Set, persistent: true), V("shortcuts", StoryVariableKind.Set, persistent: true), V("helped", StoryVariableKind.Boolean) });
            Character(d, "porter", "Porter Oren", "Remembers the first twelve bells and forgets the thirteenth. His morning shift ends four minutes into each loop.");
            Character(d, "maker", "Clockmaker Nessa", "Built the regulator. She leaves the market at the same time the porter leaves the station.");
            Character(d, "archivist", "Archivist Pell", "Keeps the records of a day that officially never happened.");
            Character(d, "courier", "Courier Ash", "Carries a key through town after the fourth minute. The address on the parcel changes every loop.");
            Character(d, "keeper", "Bell Keeper Eda", "Can stop the bell, but needs evidence that stopping it will release the town safely.");
            var start = Node(d, "town-square", StoryNodeKind.Choice, 1, "keeper", "Twelve minutes before the thirteenth bell",
                "At noon the town clock strikes thirteen, and morning begins again. You remember. The people around you do not.\n\nThe porter and clockmaker both leave their posts after four minutes. The courier appears after four; the rooftop signal follows after five. Choose an investigation. Knowledge and discovered shortcuts survive the bell, while appointments and favors begin again. Return here after each lead to plan your remaining time.");
            var data = new[] {
                new[] { "station", "porter", "A ticket with tomorrow's date", "The porter hands you a ticket stamped tomorrow. Its serial number matches the regulator housing. He insists the courier collected an identical ticket yesterday, then admits he cannot remember yesterday afternoon.", "ticket", "serial" },
                new[] { "market", "maker", "The maker's unfinished warning", "Nessa drew two circuits in her notebook. One repeats the clock. The other releases it. Someone copied the first and erased the label from the second. She tells you to compare the archive's original bell chart.", "maker", "release" },
                new[] { "archive", "archivist", "A missing line in the bell chart", "The chart records twelve ordinary strikes and one sustained signal. The thirteenth is an instruction to the regulator, not a bell for the town. A marginal note names the rooftop relay.", "chart", "relay" },
                new[] { "workshop", "maker", "The regulator is listening", "Inside the housing, the regulator waits for a signal from the rooftop. Its governor is intact. The fault is in the command it receives. You mark the release circuit and memorize a faster way through the service door.", "regulator", "governor" },
                new[] { "courier", "courier", "The parcel addressed to nobody", "Ash waits until the fourth minute, then takes a parcel to the archive. You recognize the seal from the porter's ticket. Inside is the vault key. Ash lets you copy its shape when you explain that the delivery will repeat forever otherwise.", "key", "seal" },
                new[] { "rooftop", "keeper", "A signal across the roofs", "The relay flashes the repeat command just before the bell. You trace its source to a sealed archive register. Disconnecting it would silence the command, but the regulator could stop with the town still trapped between mornings.", "signal", "relay" },
                new[] { "vault", "archivist", "The first morning", "The register records an emergency experiment intended to save the town from a failed regulator. The operator sent repeat instead of release. The safe correction requires the original chart, a tested governor, and the release circuit together.", "origin", "release" },
                new[] { "bell", "keeper", "A safe correction", "Eda tests the governor with the bell disconnected. It can release the stored time without a shock if the command is corrected first. She asks you to bring proof of the original sequence before risking the whole town.", "safe", "governor" }
            };
            foreach (var row in data)
            {
                Choice(d, start, "investigate:" + row[0], "Investigate — " + row[0], row[0] + "-result");
                var n = Node(d, row[0] + "-result", StoryNodeKind.Passage, 1, row[1], row[2], row[3], "town-square");
                n.Effects.Add(Include("shortcuts", row[0]));
                foreach (var fact in new[] { row[4], row[5] })
                {
                    n.Effects.Add(Include("facts", fact));
                    if (!d.Journal.Exists(j => j.Id == fact)) Evidence(d, fact, fact.ToUpperInvariant(), row[3]);
                    n.Journal.Add(fact);
                }
            }
            var solve = Choice(d, start, "solve", "Present the complete sequence and correct the command.", "ending-release");
            solve.Requirement = new StoryCondition { Kind = ConditionKind.All, Children = new List<StoryCondition> { Has("facts", "origin"), Has("facts", "safe"), Has("facts", "chart") } };
            solve.Unavailable = UnavailableChoice.Locked; d.Text["solve.reason"] = "Find the origin, test a safe shutdown, and recover the bell chart."; solve.DisabledReasonKey = "solve.reason";
            var silence = Choice(d, start, "silence", "Disconnect the rooftop relay without correcting the regulator.", "ending-still"); silence.Requirement = Has("facts", "signal");
            Choice(d, start, "accept", "Leave the bell untouched and record what you have learned.", "ending-repeat");
            Node(d, "ending-release", StoryNodeKind.Ending, 1, "keeper", "The first afternoon", "Eda sends the release command. The clock strikes twelve times and stops. For a moment the town holds its breath. Then the minute hand moves forward.\n\nAt the station, Oren tears up tomorrow's ticket. Nessa opens her workshop for the afternoon. The courier asks where to take a parcel, and nobody already knows the answer.");
            Node(d, "ending-still", StoryNodeKind.Ending, 1, "keeper", "A quiet town", "The relay goes dark. The thirteenth bell never rings, but the minute hand never reaches noon. You have stopped the repetition without releasing the time inside it.\n\nEda stays beside the silent clock. Somewhere in the archive is the instruction you still need. This ending records a solution to the noise, not to the town's captivity.");
            Node(d, "ending-repeat", StoryNodeKind.Ending, 1, "archivist", "A record for another morning", "You leave a clear account beside the bell chart. When noon arrives, the town returns to morning. Your memory remains.\n\nThe experiment is unresolved, but its witnesses now have a record. Another investigation can begin with better questions.");
            return d;
        }
    }
}
