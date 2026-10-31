using System.Collections.Generic;
using IncrementalFramework.Features.Narrative;

namespace IncrementalFramework.Samples.Expanded
{
    public static partial class ExpandedCampaigns
    {
        public static StoryDefinition Floodwall()
        {
            var d = new StoryDefinition { Id = "floodwall-v1", Title = "Letters from the Floodwall", Start = "appointment" };
            d.Variables.AddRange(new[] { V("preparedness", StoryVariableKind.Integer), V("trust", StoryVariableKind.Integer, 1),
                V("mayor", StoryVariableKind.Integer), V("engineer", StoryVariableKind.Integer), V("ferryman", StoryVariableKind.Integer),
                V("doctor", StoryVariableKind.Integer), V("journalist", StoryVariableKind.Integer), V("facts", StoryVariableKind.Set),
                V("promises", StoryVariableKind.Set), V("kept", StoryVariableKind.Set), V("published", StoryVariableKind.Boolean) });
            Character(d, "mayor", "Mayor Iona Vale", "An elected caretaker who remembers the panic during the last flood. She believes a coordinated message saves lives.");
            Character(d, "engineer", "Engineer Sera Penn", "Keeper of the floodwall gauges. She trusts measurements but needs workers to act on them.");
            Character(d, "ferryman", "Ferryman Tomas Reed", "Runs the night crossings. His crews know every inhabited stair below the official waterline.");
            Character(d, "doctor", "Dr. Ada Marr", "Operates the riverside clinic. Moving patients without preparation can be as dangerous as staying.");
            Character(d, "journalist", "Reporter Elian Quill", "Prints a neighborhood bulletin. His sources fear that a warning will become an excuse to abandon their homes.");
            Evidence(d, "gauge", "The rising gauge", "The north gauge rose six marks overnight. The published report uses yesterday's reading.");
            Evidence(d, "ledger", "Maintenance ledger", "The lower sluice inspection was deferred twice. Its chain has never been tested under a full river.");
            Evidence(d, "witness", "A ferryman's witness", "Tomas saw water entering the service tunnel while the main wall remained dry.");
            Evidence(d, "mill", "Mill allotment", "The mill has timber and a portable pump. Its owner will release them when an officer signs the requisition.");
            Evidence(d, "map", "Unregistered households", "Thirty-two households live below the survey line. They are absent from the city's evacuation count.");
            Evidence(d, "clinic", "Clinic transfer list", "Six patients require boats with room for stretchers. A public quay can handle only two at a time.");
            Evidence(d, "chain", "Sluice chain test", "The chain will hold a partial opening. A full opening risks losing the gate entirely.");
            Evidence(d, "boats", "Night fleet", "Fishing skiffs can reach the back stairs if their owners receive a promise of fuel and safe return.");
            Evidence(d, "archive", "The old flood", "The previous council blamed panic, but its private minutes describe a warning issued too late.");
            Evidence(d, "signal", "Bell signals", "One bell means prepare, two means move, three means the lower crossing has closed. The neighborhoods already know these signals.");
            Evidence(d, "forecast", "Upstream forecast", "A second crest will arrive after midnight. A plan that survives the first surge may still fail.");
            Evidence(d, "roster", "Joint evacuation roster", "Crews, boats, patients, and unregistered households can be coordinated on a single shared list.");
            d.Text["trust.up"] = "Public trust increased."; d.Text["prepared.up"] = "Evacuation preparedness increased.";
            d.Text["promise.made"] = "Promise recorded: protect the riverside clinic.";
            d.Text["needs.map"] = "You need evidence of the unregistered households.";
            d.Text["hint.publish"] = "Residents will receive the warning; the mayor will lose control of its timing.";
            var n = Node(d, "appointment", StoryNodeKind.Passage, 1, "mayor", "Your temporary appointment",
                "The river has climbed the first stair. I am giving you the correspondence desk because every office has sent me a different account. Read the originals. Send replies in your own name.\n\nThe city cannot evacuate on rumor, and it cannot survive on reassurance alone. By evening I need a plan that the boat crews, clinic, and wall workers will actually follow. Your first packet is beside the maintenance ledger.", "ledger"); n.Checkpoint = true;
            n = Node(d, "ledger", StoryNodeKind.Passage, 1, "engineer", "A measurement is not a reassurance",
                "The [[gauge|north gauge]] has risen six marks. The circular on your desk says two. That was true yesterday. Please retain both documents; the difference is the point.\n\nThe [[ledger|lower sluice inspection]] was deferred again. I can send a crew to test its chain, or reinforce the service tunnel. I cannot do both without the mill's timber. Ask who signed the deferral before someone calls this an unforeseeable failure.", "first-warning"); n.Journal.AddRange(new[] { "gauge", "ledger" });
            n = Node(d, "first-warning", StoryNodeKind.Choice, 1, "journalist", "May I print the gauge reading?",
                "I have tomorrow's paper ready, which may be a luxury we no longer possess. Give me a verified number and I will print it tonight. Give me an instruction to wait and I will tell people that instruction came from you.\n\nA warning without a route can frighten people. Silence without a route leaves them asleep. Which risk are you willing to put your name beneath?");
            var c = Choice(d, n, "publish", "Publish the current reading and explain what remains uncertain.", "whistleblower", Add("trust", 1, "trust.up"), Add("journalist", 1), new StoryEffect { Kind = StoryEffectKind.SetBoolean, Variable = "published", Boolean = true }); c.HintKey = "hint.publish";
            Choice(d, n, "verify-first", "Verify the tunnel report before issuing a coordinated notice.", "verification", Add("preparedness", 1, "prepared.up"), Add("mayor", 1));
            Node(d, "whistleblower", StoryNodeKind.Passage, 1, "journalist", "The name beneath the number",
                "The compositor asks whether the engineer consented to being named. I have removed her signature from the proof and left the measurement. Facts should be public; frightened sources do not have to be.\n\nTomas has sent a runner. He says the tunnel is already taking water. He will speak to you, but not to a council committee.", "verification");
            n = Node(d, "verification", StoryNodeKind.Passage, 1, "ferryman", "Water under the wall",
                "I tied up beside the service tunnel at dawn. Water was coming out, not going in. The wall is holding and the city is flooding anyway. Put that in whatever language your office requires.\n\nThere are families below the survey line. We ferry their children every school morning, so please do not tell me your register says nobody lives there.", "witness-response"); n.Journal.Add("witness");
            n = Node(d, "witness-response", StoryNodeKind.Choice, 1, "ferryman", "Whose names go on the list?",
                "I can give you the household names. Some families have good reasons to avoid official lists. Promise that this is an evacuation register, not a rent inspection, and I will bring the whole book.");
            Choice(d, n, "protect-register", "Keep the household list with the rescue crews and protect its other uses.", "mill", Add("trust", 1, "trust.up"), Add("ferryman", 1), Include("facts", "households"));
            Choice(d, n, "official-register", "Require the names in the official register to coordinate supplies.", "mill", Add("preparedness", 1, "prepared.up"), Add("mayor", 1));
            n = Node(d, "mill", StoryNodeKind.Passage, 1, "engineer", "A signature releases the timber",
                "The mill owner is waiting for a requisition. His pump would keep the clinic approach dry; his timber could brace the tunnel. He asks who will pay if neither comes back.\n\nI have attached the inventory rather than his argument. We need decisions about actual objects now: one pump, six carts, thirty beams. The river will not accept an undertaking in place of a beam.", "sluice"); n.Journal.Add("mill");
            n = Node(d, "sluice", StoryNodeKind.Passage, 1, "doctor", "Patients are not cargo",
                "Six patients cannot walk to the upper ward. Each needs a stretcher boat and someone who knows their medication. If you send a general evacuation order, healthy families will fill the quay before my attendants arrive.\n\nPromise a protected transfer window or tell me plainly that I must make another arrangement. I can work with an unwelcome answer. I cannot work with a promise nobody intends to keep.", "triage"); n.Journal.Add("clinic");
            n = Node(d, "triage", StoryNodeKind.Choice, 1, "doctor", "The first allocation",
                "The requisition form has two lines. On the first, tell the mill where to send the pump. On the second, tell me whether I should prepare the patients for transfer. The crews are waiting for the same signature.");
            Choice(d, n, "clinic-promise", "Send the pump to the clinic and promise a protected transfer.", "chapter-one-close", Include("promises", "clinic", "promise.made"), Add("doctor", 1), Add("trust", 1, "trust.up"));
            Choice(d, n, "wall-first", "Brace the tunnel first; tell the clinic it must arrange its own window.", "chapter-one-close", Add("preparedness", 2, "prepared.up"), Add("engineer", 1));
            Node(d, "chapter-one-close", StoryNodeKind.Passage, 1, "mayor", "Two routes through the same night",
                "The first replies have gone. The crews now know where your signature carries weight. Sera wants you at the sluice briefing. Tomas wants you at the neighborhood landing. Neither meeting can wait for the other.\n\nYour earlier decisions have determined who will share their plan with you. Whichever door opens, remember that the people behind the other one will still be here when the water arrives.", "route");
            n = Node(d, "route", StoryNodeKind.Branch, 2, null, "", "", "council-briefing");
            n.Branches.Add(new StoryBranch { Condition = At("trust", 3), Target = "landing-briefing" });
            n = Node(d, "council-briefing", StoryNodeKind.Passage, 2, "engineer", "The sluice briefing",
                "The chain test held at half travel. At full travel the gate shudders. We can reduce pressure by opening halfway, but the lower street will flood deliberately.\n\nI need an officer to take responsibility for that street's evacuation before I touch the wheel. Engineering is not permission to decide who gets wet.", "council-allocation"); n.Checkpoint = true; n.Journal.Add("chain");
            n = Node(d, "council-allocation", StoryNodeKind.Choice, 2, "mayor", "The street below the gate",
                "The official count says the lower street can be cleared in twenty minutes. Tomas says it will take twice that. We can delay the opening, or accept a shorter window and reserve more workers for the wall.");
            Choice(d, n, "long-window", "Use the ferrymen's count and delay until every stair has been checked.", "council-archive", Add("trust", 1, "trust.up"), Add("ferryman", 1));
            Choice(d, n, "short-window", "Use the official count and reinforce the wall immediately.", "council-archive", Add("preparedness", 2, "prepared.up"), Add("mayor", 1));
            n = Node(d, "council-archive", StoryNodeKind.Passage, 2, "journalist", "The minutes nobody printed",
                "A clerk found the old flood minutes. The public account blamed panic. The private account says the warning arrived after the boats had stopped running.\n\nYou do not have to repeat their conclusion merely because the same room is making it. I am sending a copy to the desk, not to the press. Decide what this evidence changes before deciding what it proves.", "council-requisition"); n.Journal.Add("archive");
            n = Node(d, "council-requisition", StoryNodeKind.Choice, 2, "engineer", "Reserve crews",
                "The reserve crew can escort clinic boats or reinforce the eastern gate. Both tasks improve someone's chances. Only one can be done before the crest.");
            Choice(d, n, "council-clinic", "Assign the reserve to the clinic transfer.", "council-signal", Include("kept", "clinic"), Add("trust", 1, "trust.up"), Add("doctor", 1));
            Choice(d, n, "council-gate", "Assign the reserve to the eastern gate.", "council-signal", Add("preparedness", 2, "prepared.up"), Add("engineer", 1));
            n = Node(d, "council-signal", StoryNodeKind.Passage, 2, "mayor", "A signal everyone knows",
                "The neighborhoods already use the old bell signals. We could issue a new code, but then people would have to learn it while listening for danger. Use the familiar bells. Put the instructions in every notice.\n\nI will stand beside the bell keeper if that makes the order credible. Your office must make sure credibility reaches the stairs below the survey line.", "council-map"); n.Journal.Add("signal");
            n = Node(d, "council-map", StoryNodeKind.Passage, 2, "ferryman", "The missing stairs",
                "Here is [[map|the map your survey missed]]. I have marked each landing, including the ones people built themselves. It is accurate because we use it, not because anyone approved it.\n\nAdd these households to the plan before you announce that everyone has a place.", "chapter-two-close"); n.Journal.Add("map"); n.Effects.Add(Include("facts", "households"));
            n = Node(d, "landing-briefing", StoryNodeKind.Passage, 2, "ferryman", "The neighborhood landing",
                "The skiff owners came because they heard your office would listen. There are more boats than the council counted, but the owners need fuel and a guarantee that officials will let them back through the cordon.\n\nDo not requisition their livelihoods and call it cooperation. Tell them what you need and what the city owes them in return.", "landing-bargain"); n.Checkpoint = true; n.Journal.Add("boats");
            n = Node(d, "landing-bargain", StoryNodeKind.Choice, 2, "ferryman", "A fleet or a favor",
                "The owners ask for a signed fuel guarantee. You can make that promise and coordinate a fleet, or ask for volunteers and keep the city's remaining funds for the wall.");
            Choice(d, n, "fleet-guarantee", "Guarantee fuel and return passage for every participating skiff.", "landing-households", Add("trust", 1, "trust.up"), Add("ferryman", 1));
            Choice(d, n, "fleet-volunteers", "Ask volunteers to take the first crossing; reserve funds for repairs.", "landing-households", Add("preparedness", 2, "prepared.up"));
            n = Node(d, "landing-households", StoryNodeKind.Passage, 2, "journalist", "A city larger than its register",
                "I have spoken to the families on Tomas's map. Several thought the evacuation notice did not apply to them because their street was not named. Others feared the upper shelter would demand papers.\n\nWrite the next notice so that it answers both fears. You are not merely transmitting information. You are telling people whether the city considers them its own.", "landing-clinic"); n.Journal.Add("map"); n.Effects.Add(Include("facts", "households"));
            n = Node(d, "landing-clinic", StoryNodeKind.Choice, 2, "doctor", "The narrow boats",
                "Two fishing skiffs can take the stretchers if we remove their nets. That leaves fewer seats on the first crossing. I will not hide that trade from the waiting families. Will your office explain it with me?");
            Choice(d, n, "landing-patients", "Protect the patient crossing and explain the delay to the families.", "landing-archive", Include("kept", "clinic"), Add("doctor", 1), Add("trust", 1, "trust.up"));
            Choice(d, n, "landing-capacity", "Use every seat for the first evacuation; arrange patients afterward.", "landing-archive", Add("preparedness", 2, "prepared.up"));
            n = Node(d, "landing-archive", StoryNodeKind.Passage, 2, "journalist", "The old excuse",
                "The old minutes reached the landing before they reached the mayor. People remember being blamed for panic. They are asking whether this warning will arrive in time.\n\nGive them a route and a signal, not a defense of the institution. Tomas has already chalked the bell code onto the shelter door.", "landing-signal"); n.Journal.Add("archive");
            n = Node(d, "landing-signal", StoryNodeKind.Passage, 2, "ferryman", "Bells across the water",
                "One to prepare. Two to move. Three when the lower crossing closes. The skiffs can carry that message farther than a printed order if every landing repeats it.\n\nThe crews are ready. Now send us the same roster the wall workers use, so that a closed gate does not cut off an open route.", "chapter-two-close"); n.Journal.Add("signal");
            n = Node(d, "chapter-two-close", StoryNodeKind.Passage, 2, "engineer", "One plan, two kinds of knowledge",
                "Your reports finally agree on something: the second crest will be worse. The sluice calculation assumes the lower street is clear. The boat plan assumes the eastern gate holds. Neither plan survives alone.\n\nI have put both on the same sheet. Please keep them there when the arguments begin again.", "final-packet"); n.Journal.Add("forecast");
            n = Node(d, "final-packet", StoryNodeKind.Passage, 3, "mayor", "Before the second crest",
                "There will be no further committee. The final packet contains the roster, your earlier promises, and every reply received. Read them before you issue the last order.\n\nPreparedness stands at {preparedness}; public trust stands at {trust}. These are signs of what people can do together, not guarantees. The final decision still belongs to you.", "evidence-review"); n.Checkpoint = true;
            n = Node(d, "evidence-review", StoryNodeKind.Choice, 3, "journalist", "What goes in the public notice?",
                "The public notice can carry the full household map, the technical plan, or a shorter instruction. A useful notice tells people where they belong and what they must do next.");
            c = Choice(d, n, "shared-roster", "Publish a shared roster covering every household and rescue crew.", "promise-check", Add("preparedness", 1, "prepared.up"), Add("trust", 1, "trust.up"));
            c.Requirement = Has("facts", "households"); c.Unavailable = UnavailableChoice.Locked; c.DisabledReasonKey = "needs.map";
            Choice(d, n, "technical-notice", "Publish the gate schedule and official shelter assignments.", "promise-check", Add("preparedness", 2, "prepared.up"));
            Choice(d, n, "personal-notice", "Ask each landing captain to deliver a personal warning.", "promise-check", Add("trust", 1, "trust.up"));
            Choice(d, n, "minimal-notice", "Issue a brief reassurance and withhold the contested plans until after the crest.", "promise-check", Add("trust", -2), Add("preparedness", -2));
            n = Node(d, "promise-check", StoryNodeKind.Branch, 3, null, "", "", "promise-unsettled");
            n.Branches.Add(new StoryBranch { Condition = Has("kept", "clinic"), Target = "promise-kept" });
            n = Node(d, "promise-kept", StoryNodeKind.Passage, 3, "doctor", "Six names crossed off safely",
                "All six patients are on the upper ward. The crews kept the transfer window open, and the families understood why. I have sent the freed boats back to Tomas.\n\nYou may count this promise as kept. Please remember that the result depended on the attendants, the boat owners, and the people who waited without pushing ahead.", "evacuation"); n.Journal.Add("roster");
            n = Node(d, "promise-unsettled", StoryNodeKind.Passage, 3, "doctor", "The transfer still waiting",
                "The patients remain at the quay. We have made another arrangement, but it is slower and uses boats that were meant for the last households.\n\nIf you promised a window, this is the consequence of losing it. If you refused one, this is the cost you asked us to bear. Either way, write the patients into the final order.", "evacuation");
            n.Variants.Add(new StoryTextVariant { Condition = Has("promises", "clinic"), TextKey = "broken-promise.body" });
            d.Text["broken-promise.body"] = "You promised a protected transfer. My attendants prepared for it. The boats were reassigned without telling us. We are improvising now, and people who trusted your word are paying for the delay.\n\nThere is still time to help. Acknowledge the broken promise in the final order and give the landing crews the authority to finish the transfer.";
            n = Node(d, "evacuation", StoryNodeKind.Choice, 3, "mayor", "The final order",
                "The bell keeper has a hand on the rope. Sera waits at the wheel. Tomas waits at the landing. Ada waits beside the stretchers. Elian has left a blank space for your words.\n\nYou cannot remove the flood. Choose how the city meets it.");
            Choice(d, n, "joint-command", "Give the wall crew and landing captains joint authority under one public plan.", "outcome", Add("preparedness", 1, "prepared.up"), Add("trust", 1, "trust.up"));
            Choice(d, n, "central-command", "Centralize control at the wall and prioritize the gate schedule.", "outcome", Add("preparedness", 2, "prepared.up"), Add("trust", -2));
            Choice(d, n, "local-command", "Let the neighborhoods choose their own routes; keep boats available to them.", "outcome", Add("trust", 2, "trust.up"), Add("preparedness", -2));
            n = Node(d, "outcome", StoryNodeKind.Branch, 3, null, "", "", "ending-fracture");
            n.Branches.Add(new StoryBranch { Condition = new StoryCondition { Kind = ConditionKind.All, Children = new List<StoryCondition> { At("preparedness", 4), At("trust", 4) } }, Target = "ending-together" });
            n.Branches.Add(new StoryBranch { Condition = At("preparedness", 4), Target = "ending-wall" });
            n.Branches.Add(new StoryBranch { Condition = At("trust", 4), Target = "ending-river" });
            Node(d, "ending-together", StoryNodeKind.Ending, 3, "journalist", "The city that answered",
                "The second crest took the lower street and left the shelters standing. The shared roster let crews find the last households before the third bell. Not every beam held. Enough people knew the plan to adapt when one failed.\n\nIn the morning, your correspondence desk becomes a claims desk. Promises made during an emergency do not expire with the water. The first letter asks when the skiff owners will receive their fuel.");
            Node(d, "ending-wall", StoryNodeKind.Ending, 3, "engineer", "A wall that held",
                "The gates survived. The upper city remained dry. On the lower stairs, orders arrived faster than explanations, and several crews worked outside the official plan.\n\nThe mayor calls the operation a success. The inquiry will ask whose absence made the final count look clean. Sera sends you the measurements without a covering note. They tell one part of what happened. The letters tell the rest.");
            Node(d, "ending-river", StoryNodeKind.Ending, 3, "ferryman", "A city carried by its people",
                "The neighborhood fleet brought people out through routes the official map never showed. The eastern gate failed before dawn. Workshops and homes stood in the water, but their occupants stood together on the upper quay.\n\nRebuilding will be harder because some preparations were never made. Tomas asks you to keep the household map. The city should not have to rediscover its own people at the next flood.");
            Node(d, "ending-fracture", StoryNodeKind.Ending, 3, "doctor", "The letters that arrived late",
                "The plans did not join in time. Boats waited at a closed gate while workers waited for a cleared street. The clinic's last transfer reached the upper ward after the second crest.\n\nAt dawn, survivors bring names to your desk. You keep the conflicting orders beside them. The inquiry must begin with what the city knew, what it promised, and what it failed to do. You open a fresh ledger.");
            return d;
        }
    }
}
