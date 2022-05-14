﻿using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        private readonly HttpClient _client;

        public static string RandomCatFact => _catFacts[Random.Shared.Next(_catFacts.Count)];

        public CatFactsCommand(ILogger logger, HttpClient client) : base(logger)
        {
            _client = client;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Get current subscribers asynchronously and add this one to the list if they do not exist
            _ = Task.Run(async () =>
            {
                var currentSubscribers = await GetSubscribersAsync(_client);
                if (!currentSubscribers.Any(s => s.IDNum == command.User.Id))
                {
                    currentSubscribers.Add(new Subscriber { ID = command.User.Id.ToString(), FriendlyName = command.User.Username, DateSubscribed = DateTimeOffset.Now, TimesUnsubscribed = 0 });
                    await _client.PutAsync(Environment.GetEnvironmentVariable("CatFactsSubscribers"), JsonContent.Create(currentSubscribers));
                }
            });

            return await Task.FromResult(command.Respond(RandomCatFact));
        }

        public static async Task<List<Subscriber>> GetSubscribersAsync(HttpClient client)
        {
            string subscribersUrl = Environment.GetEnvironmentVariable("CatFactsSubscribers");
            var response = await client.GetAsync(subscribersUrl);
            string content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<Subscriber>>(content);
        }

        public static async Task UpdateSubscribersAsync(List<Subscriber> subscribers, HttpClient client)
        {
            string json = JsonSerializer.Serialize(subscribers);
            var content = new StringContent(json, null, MediaTypeNames.Application.Json);
            await client.PutAsync(Environment.GetEnvironmentVariable("CatFactsSubscribers"), content);
        }

        private static readonly IReadOnlyList<string> _catFacts = new List<string>
        {
            "A house cat’s genome is 95.6 percent tiger, and they share many behaviors with their jungle ancestors, says Layla Morgan Wilde, a cat behavior expert and the founder of Cat Wisdom 101. These behaviors include scent marking by scratching, prey play, prey stalking, pouncing, chinning, and urine marking.",
            "Cats are believed to be the only mammals who don’t taste sweetness.",
            "Cats are nearsighted, but their peripheral vision and night vision are much better than that of humans.",
            "Cats are supposed to have 18 toes (five toes on each front paw; four toes on each back paw).",
            "Cats can jump up to six times their length.",
            "Cats’ claws all curve downward, which means that they can’t climb down trees head-first. Instead, they have to back down the trunk.",
            "Cats’ collarbones don’t connect to their other bones, as these bones are buried in their shoulder muscles.",
            "Cats have 230 bones, while humans only have 206.",
            "Cats have an extra organ that allows them to taste scents on the air, which is why your cat stares at you with her mouth open from time to time.",
            "Cats have whiskers on the backs of their front legs, as well.",
            "Cats have nearly twice the amount of neurons in their cerebral cortex as dogs.",
            "Cats have the largest eyes relative to their head size of any mammal.",
            "Cats make very little noise when they walk around. The thick, soft pads on their paws allow them to sneak up on their prey — or you!",
            "Cats’ rough tongues can lick a bone clean of any shred of meat.",
            "Cats use their long tails to balance themselves when they’re jumping or walking along narrow ledges.",
            "Cats use their whiskers to 'feel' the world around them in an effort to determine which small spaces they can fit into. A cat’s whiskers are generally about the same width as its body. (This is why you should never, EVER cut their whiskers.)",
            "Cats walk like camels and giraffes: They move both of their right feet first, then move both of their left feet. No other animals walk this way.",
            "Male cats are more likely to be left-pawed, while female cats are more likely to be right-pawed.",
            "Though cats can notice the fast movements of their prey, it often seems to them that slow-moving objects are actually stagnant.",
            "Some cats are ambidextrous, but 40 percent are either left- or right-pawed.",
            "Some cats can swim.",
            "There are cats who have more than 18 toes. These extra-digit felines are referred to as being 'polydactyl.'",
            "A cat’s average lifespan increased by a year over the span of time between 2002 and 2012, according to a study by Banfield Pet Hospital.",
            "According to The Huffington Post, cats typically sleep for 12 to 16 hours a day.",
            "Cats are crepuscular, which means that they’re most active at dawn and dusk.",
            "Cats are fastidious creatures about their 'bathroom.' If you have more than one cat, you should have one litter box for each.",
            "Cats can spend up to a third of their waking hours grooming.",
            "Cats live longer when they stay indoors.",
            "Cats’ purring may be a self-soothing behavior, since they make this noise when they’re ill or distressed, as well as when they’re happy.",
            "Cats will refuse an unpalatable food to the point of starvation.",
            "Despite popular belief, many cats are actually lactose intolerant.",
            "Female cats have the ability to get pregnant when they are only 4 months old!",
            "Grapes and raisins, as well as onions, garlic, and chives, are all extremely harmful foods for cats. Grapes and raisins can cause kidney failure — although the reasoning behind that isn’t clear. Meanwhile, onions, garlic, and chives wreak havoc on your cat’s gastrointestinal system and can cause anemia.",
            "If you keep your cat active during the day, he will sleep better at night. If you’re not free-feeding your cat, you can also help her get a good night’s sleep by providing her with a substantial evening meal.",
            "It’s believed that catnip produces an effect similar to LSD or marijuana in cats. The effects of nepetalactone — the chemical in catnip that can makes cats crazy — wears off within 15 minutes, and won’t surface again for a few hours, even if your cat remains in sniffing distance.",
            "Kittens can be spayed or neutered when they are only eight weeks old. If possible, these procedures should be performed in the first 5 months of your cat’s life.",
            "Male cats who have been fixed need fewer calories to maintain their weight.",
            "Spaying and neutering can extend a cat’s life. The Banfield Pet Hospital study found that neutered males live an average of 62 percent longer than unneutered cats and spayed females live an average of 39 percent longer than unspayed cats.",
            "Your cat’s grooming process stimulates blood flow to his skin, regulates his body temperature and helps him relax.",
            "A cat with a question-mark-shaped tail is asking, 'Want to play?'",
            "According to Wilde, a slow blink is a 'kitty kiss.' This movement shows contentment and trust.",
            "Cats have a unique 'vocabulary' with their owner — each cat has a different set of vocalizations, purrs and behaviors.",
            "Cats have up to 100 different vocalizations — dogs only have 10.",
            "Cats find it threatening when you make direct eye contact with them.",
            "Cats mark you as their territory	when they rub their faces and bodies against you, as they have scent glands in those areas.",
            "Cats may yawn as a way to end a confrontation with another animal. Think of it as their 'talk to the hand' gesture.",
            "Hissing is defensive, not aggressive, says Wilde. 'It’s an expression of fear, stress or discomfort of a threatened cat communicating ‘stay away,'' she says.",
            "If cats are fighting, the cat that’s hissing is the more vulnerable one, says Wilde.",
            "If your cat approaches you with a straight, almost vibrating tail, this means that she is extremely happy to see you.",
            "Kneading — which some people refer to as 'making biscuits' — is a sign of contentment and happiness. Cats knead their mothers when they are nursing to stimulate the let-down of milk.",
            "Meowing is a behavior that cats developed exclusively to communicate with people.",
            "When a cat flops over and exposes his belly, it’s not always an invitation for a belly rub. A cat does this when he’s relaxed and showing trust.",
            "When cats hit you with retracted claws, they’re playing, not attacking.",
            "When dogs wag their tails, they may be expressing happiness. But this isn’t the case for cats! When your cat wags her tail, it’s her way of warning you that you are getting on her last nerve.",
            "When your cat sticks his butt in your face, he is doing so as a gesture of friendship.",
            "Whiskers are also good indicators of a cat’s mood. When a cat is scared, he put his whiskers back. But when a cat is in hunting mode, he puts his whiskers forward.",
            "Your cat drapes its tail over another cat, your dog, or you as a symbol of friendship.",
            "Cats are very fussy about their water bowls; some prefer to ignore their bowls entirely in favor of drinking from the sink faucet.",
            "Cats groom other cats — and sometimes people — in a ritual called allogrooming.",
            "Cats like to sleep on things that smell like their owners, such as their pillows and dirty laundry (ick!).",
            "Cats love to sleep in laundry baskets, too, because they’re basically hiding places with peep holes.",
            "Cats often attack your ankles when they’re bored.",
            "Certain cats go crazy for foods you wouldn’t expect, like olives, potato chips, and the hops in beer.",
            "For some reason, cats really dislike citrus scents.",
            "If you can’t find your cat, you should look in a box or a bag, as these are some of their favorite hiding spots!",
            "Male cats who try to get to a female in heat can show very bizarre behavior — for example, some have been known to slide down chimneys!",
            "Many cats like to lick their owner’s freshly washed hair.",
            "Some cats love the smell of chlorine.",
            "Thieving behavior is not uncommon among cats. They will often grab objects like stuffed animals, feather dusters, and other things that remind them of prey.",
            "A green cat was born in Denmark in 1995. Some people believe that high levels of copper in the water pipes nearby may have given his fur a verdigris effect.",
            "It turns out that Abraham Lincoln was a crazy cat president! He had four cats that lived in the White House with him.",
            "Maria Assunta left her cat, Tomasso, her entire $13 million fortune when she died in 2011.",
            "President Bill Clinton’s cat, Socks, was a media darling during the Clinton administration and was said to receive more letters than the President himself.",
            "Stubbs, a 17-year-old orange tabby, is mayor of the historic district of Talkeetna, Alaska.",
            "A cat’s learning style is about the same as a 2- to 3-year-old child.",
            "A cat’s purr vibrates at a frequency of 25 to 150 hertz, which is the same frequency at which muscles and bones repair themselves.",
            "A group of kittens is called a 'kindle.'",
            "A house cat could beat superstar runner Usain Bolt in the 200 meter dash.",
            "About half of the cats in the world respond to the scent of catnip.",
            "Cat breeders are called 'catteries.'",
            "Cats can be toilet-trained.",
            "Cats can drink sea water in order to survive. (In case you’re wondering, we can’t.)",
            "Cats don’t have an incest taboo, so they may choose to mate with their brothers and sisters.",
            "Cats dream, just like people do.",
            "Cats have contributed to the extinction of 33 different species.",
            "Cats perceive people as big, hairless cats, says Wilde.",
            "Cats were first brought to the Americas in colonial times to get rid of rodents.",
            "Collective nouns for adult cats include 'clowder,' 'clutter,' 'glaring,' and 'pounce.'",
            "Each cat’s nose print is unique, much like human fingerprints.",
            "Every Scottish Fold cat in the world can trace its heritage back to the first one, which was found in Scotland in the 1960s, says Cheryl Hogan, a Scottish Fold breeder and the committee chair for the breed at The International Cat Association (TICA).",
            "It’s not uncommon to see cats in food stores in big cities as a form of free — and adorable — pest control.",
            "Kittens in the same litter can have more than one father. This is because the female cat releases multiple eggs over the course of a few days when she is in heat.",
            "Male cats are the most sensitive to catnip, while kittens under 3 months old have no response at all.",
            "Most world languages have a similar word to describe the 'meow' sound.",
            "People often think that they’ve stumbled over a purebred as a stray or in a shelter, but Hogan says that this is very uncommon. 'Ninety-nine times out of 100 what you have found on the street will not be purebred anything,' she says. 'Very seldom do breeders sell kittens that are not already spayed or neutered,' as purebred cats need to meet very strict standards.",
            "Some 700 million feral cats live in the United States, and many shelters run trap-neuter-release programs to stem the population growth.",
            "Studies suggest that domesticated cats first appeared around 3600 B.C.",
            "The first known cat video was recorded in 1894.",
            "There are about 88 million pet cats in the United States, which makes them the most popular pet in the country!",
            "Two hundred feral cats prowl the park at Disneyland, doing their part to control rodents — the ones who don’t wear funny outfits and speak in squeaky voices.",
            "White cats with blue eyes are prone to deafness.",
            "When cats climb a tree, they can't go back down it head first. This is because their claws are facing the same way, instead, they have to go back down backward.",
            "According to a Hebrew legend, God created cats after Noah prayed for help in protecting the food stores on the Ark from being eaten by rats. In return, God made a lion sneeze and out came a pair of cats.",
            "Your cat not only rubs their head against you as a sign of affection, but they are also making you as their territory. They use the scent glands they have around their face, the base of their tails, and their paws to do so.",
            "Cats are actually more popular in the United States than dogs are. There are around 88 million pet cats versus 75 million pet dogs.",
            "Cat's can't taste sweetness. Scientists believe it's due to a genetic mutation that affects key taste receptors.",
            "In Japan, cats are thought to have the power to turn into super spirits when they die. This may stem from the Buddist believe that cats are temporary resting places for powerful and very spiritual people.",
            "Europe introduced cats into the Americas as a form of pest control in the 1750s.",
            "There are up to 70 million feral cats in the United States alone. A good reason to spay and neuter your pets!",
            "In Holland’s embassy in Moscow, Russia, the staff noticed that the two Siamese cats kept meowing and clawing at the walls of the building. Their owners finally investigated, thinking they would find mice. Instead, they discovered microphones hidden by Russian spies. The cats heard the microphones when they turned on. Instead of alerting the Russians that they found said microphones, they simply staged conversations about delays in work orders and suddenly problems were fixed much quicker!",
            "When Ben Rea, a successful British antique dealer and known recluse died in 1988, he left his 12.5 million dollar fortune (26.7 million by today's standards) to his cat, Blackie. Making Blackie the wealthiest cat in the world. Blackie still holds the Guinness World Record for Wealthiest Cat! The money was eventually divided equally to three cat charities who were tasked with taking care of Bliackie until he passed away.",
            "Some Evidence suggests that domesticated cats have been around since 3600 B.C.E., over 2,000 years before the Ancient Egyptians.",
            "Cats only meow as a way to communicate with humans.",
            "Cats can recognize your voice. So yes, they are just ignoring you.",
            "The oldest cat video dates back to 1894 and is called 'Boxing Cats'",
            "When a family cat died in ancient Egypt, family members would shave off their eyebrows as a sign of mourning.",
            "Cats and humans have nearly identical sections of the brain that control emotions.",
            "Cats can move both ears separately and about 180 degrees around.",
            "While cats are seen as having a lower social IQ then dogs, they can slove much more difficult cognitive problems. When they feel like it of course.",
            "It was illegal to kill cats in Ancient Egypt. Not only were cats seen as an icon for Bast, the Goddess of Protection, but they were also very effective in keeping rats at bay. It was seen as a civil dis-service to kill them and often resulted in the death penalty.",
            "Abraham Lincoln kept three cats in the white house. After the civil war was over, Lincoln found 3 kittens whose mother had died and took them in as his own.",
            "Cat's, as well as other animals' noses,  have their own unique print, much like a humans fingerprint.",
            "When cats don't cover their poop, it is seen as a sign of aggression, meaning they don't fear you.",
            "Cats use their whiskers to determine if they can fit through a small space. The bigger the cat, the longer the whiskers will likely be.",
            "The Egyptian Mau is one of, if not the oldest domesticated cat breed.",
            "It is also known to be one of the fastest breeds.",
            "Mau is Egyptian for Cat.",
            "When cats bring you a dead bird or a mouse, it's not a sign of affection but instead to let you know you suck at hunting. Maybe it's a sign of affection, making sure you don't starve but still!",
            "Only 86% of U.S. Cats are spayed or neutered.",
            "Around only 24% of cats who enter a shelter end up getting adopted. (Spay and neuter your pets!)",
            "In just 10 years one female cat could produce around 49,000 kittens. Another reason to spay and neuter your pets and help support Trap-Neuter-Release programs.",
            "Cats spend nearly 1/3rd of their lives cleaning themselves.",
            "They also spend nearly 1/3rd of their lives sleeping.",
            "Blacks cats are often seen as bad luck in North American whereas, in the Uk and Australia, they are seen as good luck.",
            "A Cat's spine is so flexible because it's made up of 53 loosely fitting vertebrae. Humans only have 34.",
            "A Cat's jaw can't move sideways.",
            "When cats walk their back paws step almost exactly in the same place as the front paws did beforehand, this keeps noise to a minimum and limits visible tracks.",
            "Ever wonder why catnip lulls felines into a trance? The herb contains several chemical compounds, including one called nepetalactone, which a cat detects with receptors in its nose and mouth.",
            "More than half of the world’s felines don’t respond to catnip. Scientists still don’t know quite why some kitties go crazy for the aromatic herb and others don’t, but they have figured out that catnip sensitivity is hereditary. If a kitten has one catnip-sensitive parent, there’s a one-in-two chance that it will also grow up to crave the plant. And if both parents react to 'nip, the odds increase to at least three in four.",
            "The oldest cat ever lived for 38 years. Creme Puff of Austin, TX was born in August of 1967 and passed away in August of 2005. He still holds the Guinness World Record for oldest cat ever.",
            "The musical Cats is based on a collection of T.S. Eliot poems called Old Possum’s Book of Practical Cats.",
            "A train station in Southeastern Japan is presided over by an adorable 'stationmaster': a 6-year-old calico cat named Nitama.",
            "Polydactyl cats refer to cats with 6 toes on their front paws.",
            "Approximately 200 feral cats roam the grounds of Disneyland, where they help control the amusement park’s rodent population. They’re all spayed or neutered, and park staffers provide them with medical care and extra food.",
            "The Hungarian word for quotation marks, 'macskaköröm', literally translates to 'cat claws.'",
            "Cats can drink seawater! Their kidneys are able to filter salt out of water, something humans can't do.",
            "Contrary to public belief, adult cats shouldn't be given milk as most are lactose intolerant. After a kitten is weaned, the lactase enzyme, which is used to break down lactose,  starts to disappear. Giving your cat milk can cause an upset stomach and other tummy troubles.",
            "Cats have both short term and long term memory. This means that they can remember, short term, up to 16 hours ago.",
            "Yet they tend to be more selective compared to dogs. Meaning they only remember what is beneficial to them.",
            "Scientist suggest that a cat's purr is a method of self-healing!",
            "Cats can make more than 100 different sounds.",
            "There are 473 taste buds on a cat's tongue!",
            "Cats spend between 30 to 50 percent of their day grooming themselves.",
            "Purring doesn't always mean a cat is happy.",
            "It's possible that purring helps bone density.",
            "A cat's nose has catnip receptors.",
            "But most cats don't respond to catnip.",
            "Cats make great private detectives.",
            "Your cat probably hates music.",
            "Many historical figures loved cats.",
            "Abraham Lincoln was a huge fan of cats.",
            "If you love cats, you're an ailurophile.",
            "Cats first went to space in 1963.",
            "The world's oldest living cat is 31 years old.",
            "The Guinness World Records don't have an award for fattest cat.",
            "Cats might be marking you as territory when they massage you.",
            "There's a cat painting worth close to $1 million.",
            "Cats don't always land on their feet.",
            "America's favorite breed is the Exotic.",
            "T.S. Eliot thought cats were more poetic than dogs.",
            "Your cat might be allergic to you.",
            "Japan has a cat who manages a train station.",
            "Cheetahs aren't the only cats that are fast.",
            "Yes, ancient Egyptians loved cats.",
            "No one knows why black cats are considered to be bad luck in some cultures.",
            "In Great Britain and Japan, black cats are good luck.",
            "Nyan Cat was based on a real cat.",
            "Cats can't taste sweets.",
            "Cat shows have been around since at least 1871.",
            "Some breeds get heavy.",
            "Cute cat videos have been around for more than a century.",
            "There was a video game based on President Clinton's cat.",
            "Some cats have extra toes.",
            "Male cats have barbed penises.",
            "People who go to college are more likely to have a cat.",
            "Your cat has more bones than you do.",
            "Not all cats have fur.",
            "Most cats don't like getting wet because they lose control.",
            "But not all cats hate water.",
            "Cats like small spaces.",
            "We don't know why cats meow.",
            "Cats can sweat.",
            "Most of their lives are spent sleeping.",
            "Some hotels have lobby cats.",
            "Disneyland has a lot of feral cats (with an important job).",
            "Cats are not good at delivering mail.",
            "Quotation marks have a feline connection.",
            "There are more pet cats in the u.s. than pet dogs.",
            "Not all historical figures loved cats.",
            "Cats can jump up to five times their own height."
        };

        public class Subscriber
        {
            [JsonPropertyName("id")]
            public string ID { get; set; } // Store this as a string because jsonblob.com is zeroing out the end of ulongs for some reason

            [JsonIgnore]
            public ulong IDNum => ulong.Parse(ID);

            [JsonPropertyName("friendlyName")]
            public string FriendlyName { get; set; }

            [JsonPropertyName("dateSubscribed")]
            public DateTimeOffset DateSubscribed { get; set; }

            [JsonPropertyName("timesUnsubscribed")]
            public int TimesUnsubscribed { get; set; }
        }
    }
}