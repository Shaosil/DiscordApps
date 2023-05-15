namespace ShaosilBot.Core.Models.Whackabot
{
	public class EquipmentList
	{
		public List<Weapon> Weapons { get; set; } = new List<Weapon>();
		public List<Armor> Armors { get; set; } = new List<Armor>();

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
		public List<Attack> Attacks { get; set; } = new List<Attack>();
		public EquipmentList.Armor BotArmor { get; set; }
		public List<PlayerWeapon> PlayerWeapons { get; set; } = new List<PlayerWeapon>();
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