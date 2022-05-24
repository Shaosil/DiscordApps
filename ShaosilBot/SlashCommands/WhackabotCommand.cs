using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class WhackabotCommand : BaseCommand
    {
        private const string EquipmentFilename = "WhackabotEquipment.json";
        private const string GameFilename = "WhackabotInfo.json";

        private readonly DataBlobProvider _dataBlobProvider;
        private readonly EquipmentList _equipmentList;
        private readonly GameInfo _gameInfo;

        public WhackabotCommand(ILogger logger, DataBlobProvider dataBlobProvider) : base(logger)
        {
            _dataBlobProvider = dataBlobProvider;
            _equipmentList = JsonSerializer.Deserialize<EquipmentList>(_dataBlobProvider.GetBlobTextAsync(EquipmentFilename).GetAwaiter().GetResult());
            _gameInfo = JsonSerializer.Deserialize<GameInfo>(_dataBlobProvider.GetBlobTextAsync(GameFilename, true).GetAwaiter().GetResult());
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            Logger.LogInformation($"Whackabot Command executed at {DateTime.Now}");

            var sb = new StringBuilder();
            var playerWeapon = _gameInfo.PlayerWeapons.FirstOrDefault(p => p.PlayerID == command.User.Id);
            if (playerWeapon == null) _gameInfo.PlayerWeapons.Add(playerWeapon = new GameInfo.PlayerWeapon { PlayerID = command.User.Id });

            // Change weapon if requested, then return
            var weaponChangeRequest = command.Data.Options.FirstOrDefault(o => o.Name == "weapon-change");
            if (weaponChangeRequest != null)
            {
                string weaponName = weaponChangeRequest.Value.ToString().ToLower().Trim();
                var matchingWeapons = _equipmentList.Weapons.Where(w => w.Name.ToLower().Contains(weaponName ?? string.Empty)).ToList();
                string weaponList = string.Join(Environment.NewLine, _equipmentList.Weapons.Select(w => $"* {w.Name}"));

                if (matchingWeapons.Count != 1)
                {
                    string response = matchingWeapons.Count < 1 ? command.Respond($"Invalid weapon! Available choices:\n\n{weaponList}", ephemeral: true)
                        : command.Respond($"Multiple weapon matches found! Be a bit more specific:\n\n{weaponList}", ephemeral: true);
                    _dataBlobProvider.ReleaseFileLease(GameFilename);
                    return response;
                }

                // One match - store it and return success
                playerWeapon.Weapon = matchingWeapons.First();
                string newInfo = JsonSerializer.Serialize(_gameInfo, new JsonSerializerOptions { WriteIndented = true });
                await _dataBlobProvider.SaveBlobTextAsync(GameFilename, newInfo);
                return command.Respond($"{command.User.Mention} is now wielding *<{playerWeapon.Weapon.Name}>*!");
            }

            // If this is the first hit of the game, pick armor and reset stats
            if (!_gameInfo.GameActive)
            {
                // If it has been less than 60 seconds since the death blow, prevent a new game from starting
                if ((DateTimeOffset.Now - _gameInfo.Attacks.OrderByDescending(a => a.TimeStamp).First().TimeStamp).TotalSeconds < 60)
                {
                    _dataBlobProvider.ReleaseFileLease(GameFilename);
                    return command.Respond("*I was recently knocked out and am recovering. Please give me a minute before starting a new challenge.*", ephemeral: true);
                }

                _gameInfo.Health = 100;
                _gameInfo.Attacks = new List<GameInfo.Attack>();
                _gameInfo.BotArmor = _equipmentList.Armors[Random.Shared.Next(_equipmentList.Armors.Count)];
                _gameInfo.GameActive = true;
                sb.Append($"{command.User.Mention} approaches as a new challenger! ShaosilBot accepts, donning {_gameInfo.BotArmor.Name}. {command.User.Mention}");
                Logger.LogInformation($"New whackabot game started by {command.User.Username}. Bot chose {_gameInfo.BotArmor.Name}.");
            }
            else
                sb.Append(command.User.Mention);

            // Record damage and give a generic reaction based on remaining HP
            var reactions = new List<string>();
            int totalDamage = GetDamage(sb, playerWeapon);
            _gameInfo.Health -= totalDamage;
            if (_gameInfo.Health <= 0)
            {
                reactions.Add("ShaosilBot stares at you eerily as he slowly topples forward. He is dead before he hits the ground.");
                reactions.Add("ShaosilBot collapses into a disgusting heap of robot garbage. He is no more.");
                reactions.Add("The attack has proved too much, and the life goes out of ShaosilBot's eyes.");
                reactions.Add("You think you hear a faint 'no u' as ShaosilBot's lifeless body crumples to the ground.");
                reactions.Add("Shocked, ShaosilBot takes 5 steps backward, each more slowly than the last, before succumbing to his injuries. GG!");
                _gameInfo.GameActive = false;
            }
            else if (_gameInfo.Health <= 10)
            {
                reactions.Add("ShaosilBot appears deathly ill.");
                reactions.Add("ShaosilBot slowly looks up at you, on the verge of death.");
                reactions.Add("You can see ShaosilBot's eyes starting to glaze over.");
                reactions.Add("ShaosilBot stumbles, and collapses, before slowly standing up again, barely able to hold up his head.");
                reactions.Add("ShaosilBot has the look of death itself. His time is nearly up.");
            }
            else if (_gameInfo.Health <= 25)
            {
                reactions.Add("ShaosilBot tries to taunt you but his smile falters.");
                reactions.Add("ShaosilBot leans over to catch his breath, then resumes a fighting stance.");
                reactions.Add("ShaosilBot clutches his side, appearing winded, but with some spark left in his eyes.");
                reactions.Add("ShaosilBot steps back, eyeing you warily, with less confidence than before.");
                reactions.Add("As you advance, ShaosilBot nervously glances around, looking for a means of escape.");
            }
            else if (_gameInfo.Health <= 50)
            {
                reactions.Add("ShaosilBot takes a moment to catch his breath, then looks ready to go again.");
                reactions.Add("ShaosilBot grunts in pain, but he doesn't appear to be ready to give up quite yet.");
                reactions.Add("ShaosilBot's confidence has lessened, and with it, surely his health.");
                reactions.Add("You study ShaosilBot's stance and ascertain that he must be weakening.");
                reactions.Add("It looks as though ShaosilBot has seen better days.");
            }
            else if (_gameInfo.Health <= 75)
            {
                reactions.Add("Although ShaosilBot has taken some damage, it is obvious he has not lost his fighting spirit.");
                reactions.Add("ShaosilBot smirks at you. 'A lucky hit isn't enough to deter me!'");
                reactions.Add("ShaosilBot steps back but then regains his balance, still looking fresh.");
                reactions.Add("You jump towards ShaosilBot but he immediately counters, pushing you back.");
                reactions.Add("ShaosilBot adjusts his stance, obviously only slightly battered.");
            }
            else
            {
                reactions.Add("ShaosilBot is the absolute image of calmness, appearing ready for your next move.");
                reactions.Add("ShaosilBot laughs. 'I can do this all day!'");
                reactions.Add("You dare not take your eyes off of ShaosilBot for a second, as he appears ready to counter.");
                reactions.Add("ShaosilBot's strength is still at its peak. Another attack would be met with vigor.");
                reactions.Add("You think you see a weakness, but catch yourself before making a deadly mistake.");
            }
            sb.Append($"\n\n{reactions[Random.Shared.Next(reactions.Count)]}");

            // Stats
            if (!_gameInfo.GameActive)
            {
                sb.Append($"\n\n{command.User.Mention} has struck the final blow!");

                // Stats
                var groupedAttacks = _gameInfo.Attacks.GroupBy(a => a.PlayerID).ToDictionary(k => k.Key, v => v.ToList());
                var loadedUsers = new List<RestGuildUser>();
                foreach (var group in groupedAttacks)
                    loadedUsers.Add(await command.Guild.GetUserAsync(group.Key));

                sb.AppendLine("\n\n**PLAYER STATS ( ATs / EVs / CRTs / GRZs / DMG / ACC )**");
                foreach (var user in loadedUsers)
                {
                    var attacks = groupedAttacks[user.Id];
                    sb.Append($"{user.Mention}: {attacks.Count} / ");                                                                                       // Attacks
                    sb.Append($"{attacks.Count(a => a.HitType == GameInfo.Attack.eHitType.Miss)} / ");                                                      // Evades
                    sb.Append($"{attacks.Count(a => a.HitType == GameInfo.Attack.eHitType.Crit)} / ");                                                      // Crits
                    sb.Append($"{attacks.Count(a => a.HitType == GameInfo.Attack.eHitType.Graze)} / ");                                                     // Grazes
                    sb.Append($"{attacks.Sum(a => a.Damage)} / ");                                                                                          // Total damage
                    sb.AppendLine($"{(int)Math.Round(((float)attacks.Count(a => a.HitType != GameInfo.Attack.eHitType.Miss) / attacks.Count) * 100)}%");    // Accuracy
                }
            }

            // Update game file
            string serializedGameInfo = JsonSerializer.Serialize(_gameInfo, new JsonSerializerOptions { WriteIndented = true });
            await _dataBlobProvider.SaveBlobTextAsync(GameFilename, serializedGameInfo);

            // Prefix with skull if ded
            return command.Respond($"{(_gameInfo.Health <= 0 ? ":skull_crossbones: " : string.Empty)}{sb}");
        }

        private int GetDamage(StringBuilder sb, GameInfo.PlayerWeapon playerWeapon)
        {
            // If the player doesn't have a weapon, looks like they're using their bare hands (first in the list)
            if (playerWeapon.Weapon == null) playerWeapon.Weapon = _equipmentList.Weapons.First();

            // Choose a random type of attack (valid options are types that do > 0 damage)
            var damageTypes = new List<EquipmentList.Weapon.eDamageTypes>();
            if (playerWeapon.Weapon.BluntMaxDmg > 0) damageTypes.Add(EquipmentList.Weapon.eDamageTypes.Blunt);
            if (playerWeapon.Weapon.SlashMaxDmg > 0) damageTypes.Add(EquipmentList.Weapon.eDamageTypes.Slash);
            if (playerWeapon.Weapon.PierceMaxDmg > 0) damageTypes.Add(EquipmentList.Weapon.eDamageTypes.Pierce);
            var selectedDamageType = damageTypes[Random.Shared.Next(damageTypes.Count)];

            // Get attack verb based on weapon and damage type
            var simpleWeaponName = Regex.Replace(playerWeapon.Weapon.Name, "\\W", "").ToLower();
            switch (simpleWeaponName)
            {
                case "barehands":
                    sb.Append(" throws a punch at ShaosilBot. ");
                    break;

                case "brassknuckles":
                    sb.Append(" adjusts their brass knuckles and swings at ShaosilBot. ");
                    break;

                case "shortknife":
                    if (selectedDamageType == EquipmentList.Weapon.eDamageTypes.Blunt) sb.Append(" tries bashing ShaosilBot with the hilt of their short knife. ");
                    else if (selectedDamageType == EquipmentList.Weapon.eDamageTypes.Slash) sb.Append(" slashes at ShaosilBot with their short knife. ");
                    else sb.Append(" thrusts their short knife towards ShaosilBot. ");
                    break;

                case "club":
                    sb.Append(" swings their club at ShaosilBot. ");
                    break;

                case "shortsword":
                    if (selectedDamageType == EquipmentList.Weapon.eDamageTypes.Blunt) sb.Append(" feints and tries to catch ShaosilBot with the hilt of their short sword. ");
                    else if (selectedDamageType == EquipmentList.Weapon.eDamageTypes.Slash) sb.Append(" swings their short sword at ShaosilBot. ");
                    else sb.Append(" jumps back and tries to stab ShaosilBot with their short sword. ");
                    break;

                case "bowandarrows":
                    sb.Append($" shoots an arrow at ShaosilBot. ");
                    break;

                case "twohandedlongsword":
                    if (selectedDamageType == EquipmentList.Weapon.eDamageTypes.Slash) sb.Append(" swings their two-handed longsword at ShaosilBot. ");
                    else sb.Append(" thrusts their two-handed longsword at ShaosilBot. ");
                    break;

                case "greatslowaxeofdoomdismemberment":
                    sb.Append(" slowly swings the great slow axe of doom & dismemberment at ShaosilBot. ");
                    break;

                default:
                    sb.Append($" swings their {playerWeapon.Weapon.Name.ToLower()} at ShaosilBot. ");
                    break;
            }

            // DODGE = The to-hit roll of the attacker (max = 100 * weapon base speed) must be greater than the dodge roll of ShaosilBot (max = 50 * armor base speed)
            int maxToHit = (int)Math.Round(100 * playerWeapon.Weapon.BaseSpeed);
            int maxDodge = (int)Math.Round(50 * _gameInfo.BotArmor.BaseSpeed);
            int toHitRoll = Random.Shared.Next(maxToHit + 1);
            int dodgeRoll = Random.Shared.Next(maxDodge + 1);
            Logger.LogInformation($"{playerWeapon.Weapon.Name} to-hit roll: {toHitRoll} (max {maxToHit}). {_gameInfo.BotArmor.Name} dodge roll: {dodgeRoll} (max {maxDodge}).");

            // DAMAGE = The damage roll of the attacker (max = defined selected damage type of current weapon) minus the defense roll (max = defined selected damage type absorb of current armor)
            int maxDamage = selectedDamageType == EquipmentList.Weapon.eDamageTypes.Blunt ? playerWeapon.Weapon.BluntMaxDmg
                : selectedDamageType == EquipmentList.Weapon.eDamageTypes.Slash ? playerWeapon.Weapon.SlashMaxDmg
                : playerWeapon.Weapon.PierceMaxDmg;
            int maxDefense = selectedDamageType == EquipmentList.Weapon.eDamageTypes.Blunt ? _gameInfo.BotArmor.BluntMaxAbsorb
                : selectedDamageType == EquipmentList.Weapon.eDamageTypes.Slash ? _gameInfo.BotArmor.SlashMaxAbsorb
                : _gameInfo.BotArmor.PierceMaxAbsorb;
            int damageRoll = Random.Shared.Next(maxDamage + 1);
            int defenseRoll = Random.Shared.Next(maxDefense + 1);
            int damage = Math.Max(damageRoll - defenseRoll, 1); // Always do at least 1 damage if the bot didn't dodge

            // Graze/Crit = Damage roll is 0 or max. Crit = Resuling damage * 2
            if (damageRoll == maxDamage) damage *= 2;

            // Return 0 if we missed
            if (toHitRoll <= dodgeRoll)
            {
                sb.Append(":athletic_shoe: ShaosilBot has evaded! :athletic_shoe: ");

                // Log attack
                _gameInfo.Attacks.Add(new GameInfo.Attack
                {
                    PlayerID = playerWeapon.PlayerID,
                    Damage = 0,
                    HitType = GameInfo.Attack.eHitType.Miss,
                    WeaponName = playerWeapon.Weapon.Name,
                    TimeStamp = DateTimeOffset.Now
                });

                return 0;
            }

            Logger.LogInformation($"[{selectedDamageType.ToString().ToUpper()}] Damage roll of {damageRoll} (max {maxDamage}) over defense roll of {defenseRoll} (max {maxDefense}) results in {damage} damage.");

            // Get attack adjective based on weapon and damage type
            switch (simpleWeaponName)
            {
                case "barehands":
                    if (damageRoll == 0) sb.Append(":shield: Their hand clumsily thumps ShaosilBot. :shield:");
                    else if (damageRoll == maxDamage) sb.Append(":star: Their fist smashes into ShaosilBot's face with a satisfying *thwunk*! :star:");
                    else sb.Append(":crossed_swords: They land a solid right hook. :crossed_swords:");
                    break;

                case "brassknuckles":
                    if (damageRoll == 0) sb.Append(":shield: The brass knuckles graze against ShaosilBot. :shield:");
                    else if (damageRoll == maxDamage) sb.Append(":star: The brass knuckles smash into ShaosilBot, shoving him backwards! :star:");
                    else sb.Append(":crossed_swords: The brass knuckles connect. :crossed_swords:");
                    break;

                case "shortknife":
                    switch (selectedDamageType)
                    {
                        case EquipmentList.Weapon.eDamageTypes.Blunt:
                            if (damageRoll == 0) sb.Append(":shield: The hilt of the knife bumps into ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The hilt of the knife is slammed unceremoniously into ShaosilBot's face! :star:");
                            else sb.Append(":crossed_swords: The knife hilt attack connects. :crossed_swords:");
                            break;
                        case EquipmentList.Weapon.eDamageTypes.Slash:
                            if (damageRoll == 0) sb.Append(":shield: The knife barely cuts into ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The knife firmly runs along ShaosilBot's frame! :star:");
                            else sb.Append(":crossed_swords: The blade of the knife connects. :crossed_swords:");
                            break;
                        case EquipmentList.Weapon.eDamageTypes.Pierce:
                            if (damageRoll == 0) sb.Append(":shield: The knife pricks ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The knife plunges deep into ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The knife stab attack connects. :crossed_swords:");
                            break;
                    }
                    break;

                case "club":
                    if (damageRoll == 0) sb.Append(":shield: The club bounces into ShaosilBot. :shield:");
                    else if (damageRoll == maxDamage) sb.Append(":star: The club smashes into ShaosilBot, denting his handsome figure! :star:");
                    else sb.Append(":crossed_swords: The club smacks into ShaosilBot. :crossed_swords:");
                    break;

                case "shortsword":
                    switch (selectedDamageType)
                    {
                        case EquipmentList.Weapon.eDamageTypes.Blunt:
                            if (damageRoll == 0) sb.Append(":shield: The hilt of the sword bothers ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The hilt of the sword smashes into ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The sword hilt connects. :crossed_swords:");
                            break;
                        case EquipmentList.Weapon.eDamageTypes.Slash:
                            if (damageRoll == 0) sb.Append(":shield: The sword grazes ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The sword slices into ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The blade of the sword connects. :crossed_swords:");
                            break;
                        case EquipmentList.Weapon.eDamageTypes.Pierce:
                            if (damageRoll == 0) sb.Append(":shield: The sword pokes ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The sword is thrust firmly into ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The sword stab attack connects. :crossed_swords:");
                            break;
                    }
                    break;

                case "bowandarrows":
                    if (damageRoll == 0) sb.Append(":shield: The arrow nicks ShaosilBot. :shield:");
                    else if (damageRoll == maxDamage) sb.Append(":star: The arrow deeply pierces ShaosilBot in the knee! (He used to be an adventurer like you) :star:");
                    else sb.Append(":crossed_swords: The arrow sinks into ShaosilBot. :crossed_swords:");
                    break;

                case "twohandedlongsword":
                    switch (selectedDamageType)
                    {
                        case EquipmentList.Weapon.eDamageTypes.Slash:
                            if (damageRoll == 0) sb.Append(":shield: The longsword slightly cuts ShaosilBot. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The longsword slashes through ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The longsword attack connects. :crossed_swords:");
                            break;
                        case EquipmentList.Weapon.eDamageTypes.Pierce:
                            if (damageRoll == 0) sb.Append(":shield: The longsword pricks ShaosilBotm, drawing oil. :shield:");
                            else if (damageRoll == maxDamage) sb.Append(":star: The longsword is deftly thrust through ShaosilBot! :star:");
                            else sb.Append(":crossed_swords: The longsword thrust attack connects. :crossed_swords:");
                            break;
                    }
                    break;

                case "greatslowaxeofdoomdismemberment":
                    if (damageRoll == 0) sb.Append(":shield: The great axe connects but at an awkward angle, bouncing away after a quick slice. :shield:");
                    else if (damageRoll == maxDamage) sb.Append(":star: The great axe slams down, bringing doom towards ShaosilBot! :star:");
                    else sb.Append(":crossed_swords: The great axe connects! :crossed_swords:");
                    break;

                default:
                    sb.Append($":crossed_swords: Their {playerWeapon.Weapon.Name} attack connects. :crossed_swords:");
                    break;
            }

            // Log attack
            _gameInfo.Attacks.Add(new GameInfo.Attack
            {
                PlayerID = playerWeapon.PlayerID,
                Damage = damage,
                HitType = damageRoll == 0 ? GameInfo.Attack.eHitType.Graze : damageRoll == maxDamage ? GameInfo.Attack.eHitType.Crit : GameInfo.Attack.eHitType.Normal,
                WeaponName = playerWeapon.Weapon.Name,
                TimeStamp = DateTimeOffset.Now
            });

            return damage;
        }

        public class EquipmentList
        {
            public List<Weapon> Weapons { get; set; }
            public List<Armor> Armors { get; set; }

            public class Weapon
            {
                public enum eDamageTypes { Blunt, Slash, Pierce }

                public string Name { get; set; }
                public int BluntMaxDmg { get; set; }
                public int SlashMaxDmg { get; set; }
                public int PierceMaxDmg { get; set; }
                public float BaseSpeed { get; set; }
            }

            public class Armor
            {
                public string Name { get; set; }
                public int BluntMaxAbsorb { get; set; }
                public int SlashMaxAbsorb { get; set; }
                public int PierceMaxAbsorb { get; set; }
                public float BaseSpeed { get; set; }
            }
        }

        public class GameInfo
        {
            public int Health { get; set; }
            public List<Attack> Attacks { get; set; }
            public EquipmentList.Armor BotArmor { get; set; }
            public List<PlayerWeapon> PlayerWeapons { get; set; }
            public bool GameActive { get; set; }

            public class Attack
            {
                public enum eHitType { Miss, Graze, Normal, Crit }

                public ulong PlayerID { get; set; }
                public int Damage { get; set; }
                public eHitType HitType { get; set; }
                public string WeaponName { get; set; }
                public DateTimeOffset TimeStamp { get; set; }
            }

            public class PlayerWeapon
            {
                public ulong PlayerID { get; set; }
                public EquipmentList.Weapon Weapon { get; set; }
            }
        }
    }
}