/*
EE CM serverside codes
Copyright (C) 2013-2015 Krock/SmallJoker <mk939@ymail.com>


This library is free software; you can redistribute it and/or
modify it under the terms of the GNU Lesser General Public
License as published by the Free Software Foundation; either
version 2.1 of the License, or (at your option) any later version.

This library is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public
License along with this library; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
*/

// Compile for indev rooms
//#define INDEV

using System;
using System.Collections.Generic;
using System.IO;
using PlayerIO.GameLibrary;

namespace EE_CM {
	// Constants
	enum C {
		BLOCK_MAX = 500,
		BLOCK_TYPES = 5,
		WORLD_TYPES = 5,
		WORLDS_PER_PLAYER = 4,
		SMILIES = 64
	}

	enum Rights {
		Moderator = 6,
		Owner = 5,
		Admin = 4,
		Vigilant = 3,
		Normal = 2,
		Edit = 1,
		None = 0,
	}

#if INDEV
	[RoomType("Indev")]
#else
	[RoomType ("Game31")]
#endif
	public class EENGameCode : Game<Player> {
		#region Definition of block arrays and world data
		Bindex[,] blocks;
		Block[,] Nblock;
		Block[,,] PBlock;
		COOR[] spawnCoor;
		PlayerHistory[] usernames = new PlayerHistory[16];
		WorldInfo info = new WorldInfo ();

		int[] gravity0Id = new int[] { 4, 112, 114, 115, 116, 117, 118 };

		pList<string> banned = new pList<string> (),
			admins = new pList<string> ();

		string[] modText = new string[20],
			oldChat0 = new string[5], //names
			oldChat1 = new string[5], //text
			logbook = new string[5],
			key_colors = new string[] { "red", "green", "blue" };

		bool W_isOpen = false,
			W_isSaved = false,
			W_crownC = false,
			W_upgrade = false,
			W_isLoading = false,
			W_allowText = false,
			W_gotEdited = false,
			kill_active = false,
			isEditBlocked = false,
			W_resized = false,
			W_can_save = false,
			W_experimental_saving = false,
			W_canRespawn = true,
			W_verbose = false;

		int W_width, W_height, W_plays,
			W_crown = -1,
			W_chatLimit = 150,
			W_Bcount = 0,
			moveLimit = 20,
			cSpawn = 0,
			W_type = -1,
			sys_msg_max = 3,
			W_broadcast_level = 0;

		string W_key = "",
			W_rot13,
			W_Owner,
			W_title,
			SYS = "* SYSTEM",
			say_normal = "abcdefghijklmnopqurstuvwxyz ";

		byte[] keys = new byte[3];
		#endregion

		public override void GameStarted () {
			PreloadPlayerObjects = true;

			W_Owner = "";
			W_title = "unknown";
			W_width = 200;
			W_height = 200;
			W_plays = 0;
			W_rot13 = generate_rot13 ();

			string prefix = RoomId[0] + "" + RoomId[1];
			RoomData["plays"] = "0";
			if (prefix == "PW" || prefix == "BW") {
				W_key = generate_rot13 () + generate_rot13 ();
				load_worlddata (false, true);
			} else {
				clear_world (true);
				if (RoomData.ContainsKey ("name"))
					W_title = RoomData["name"];
				else
					RoomData["name"] = W_title = "Untitled World";

				if (RoomData.ContainsKey ("editkey")) {
					if (!string.IsNullOrEmpty (RoomData["editkey"])) {
						RoomData["needskey"] = "yup";
						W_key = RoomData["editkey"];
					} else {
						W_key = "";
						W_isOpen = true;
					}

					RoomData.Remove ("editkey");
				}
			}
			if (W_key == "")
				W_isOpen = true;
			RoomData.Save ();
			AddTimer (Keys_Timer, 200);
			AddTimer (Cleanup_Timer, 20000);
			AddTimer (initPlayers, 1500);
			AddTimer (killPlayers, 800);
		}

		public override void GameClosed () {
			if (!W_isSaved)
				return;

			PlayerIO.BigDB.Load ("Worlds", RoomId, delegate (DatabaseObject o) {
				o.Set ("name", W_title);
				o.Set ("plays", W_plays);

				string admins_text = "";
				string[] admins_array = admins.GetData ();

				for (int i = 0; i < admins_array.Length; i++) {
					if (admins_array[i] == null) continue;
					admins_text += admins_array[i] + ',';
				}

				if (admins_text.Length > 0)
					o.Set ("admins", admins_text);
				else if (o.Contains ("admins"))
					o.Remove ("admins");

				o.Save ();
			});
		}

		public override void UserJoined (Player pl) {
			string reason = HandleUserJoin (pl);

			if (reason == null)
				return;

			pl.say_counter = 99;
			pl.mWarns = 99;
			pl.Send ("info", "Connecting failed", reason);
			pl.Disconnect ();
		}

		string HandleUserJoin (Player pl) {
			long time = getMTime (),
				last_online = 0;

			for (int i = 0; i < usernames.Length; i++) {
				if (usernames[i] == null) continue;
				if (usernames[i].Id == pl.ConnectUserId) {
					last_online = Math.Max (usernames[i].join_time, last_online);
				}
			}

			if (time - last_online < 10)
				return "You create traffic and I am a traffic light.";

			if (W_resized)
				return "This world got resized. Please wait until it has fully closed.\nThanks.";

			if (!pl.PlayerObject.Contains ("name"))
				return "You need to set an username first.";

			if (pl.PlayerObject.Contains ("banned")) {
				if (!(pl.PlayerObject["banned"] is bool)) {
					long time_left = pl.PlayerObject.GetLong ("banned") - getMTime ();
					if (time_left > 20) {
						TimeSpan delta_time = new TimeSpan (time_left);
						return ("This account has been banned from EE CM.\n" +
							"Please wait " +
							delta_time.Hours +
							" hour(s) and " +
							delta_time.Minutes +
							" minute(s) until your ban expires.");
					}
				} else if (pl.PlayerObject.GetBool ("banned"))
					return "This account has been banned from EE CM.";
			}

			if (pl.PlayerObject.Contains ("isModerator"))
				pl.isModerator = pl.PlayerObject.GetBool ("isModerator");

			if (pl.PlayerObject.Contains ("isVigilant"))
				pl.isVigilant = pl.PlayerObject.GetBool ("isVigilant");

			string name = pl.PlayerObject.GetString ("name");
			if (banned.Contains (name))
				return "You have been banned from this world.";

			bool isGuest = (name == "guest");

			if (!isGuest) {
				// Something against name modification
				string allowed_chars = "abcdefghijklmnopqurstuvwxyz1234567890";
				for (int i = 0; i < name.Length; i++) {
					bool char_found = false;
					for (int c = 0; c < allowed_chars.Length; c++) {
						if (allowed_chars[c] == name[i]) {
							char_found = true;
							break;
						}
					}
					if (!char_found) {
						name = "";
						break;
					}
				}
				if (name.Length < 3 && !pl.isModerator)
					return "You are using an invalid nickname.";
			} else name = "guest-" + (pl.Id + 1);

			pl.Name = name;
			pl.isGuest = isGuest;

			// Use isGuest for non-chatters
			if (!isGuest && pl.PlayerObject.Contains ("chatbanned"))
				pl.isGuest = pl.PlayerObject.GetBool ("chatbanned");

			if (pl.PlayerObject.Contains ("face")) {
				int face = pl.PlayerObject.GetInt ("face");
				if (face < 0 || face == 31 || face > (int) C.SMILIES) pl.PlayerObject.Set ("face", 0);
				pl.Face = pl.PlayerObject.GetInt ("face");
			}

			int found = 0;
			System.Net.IPAddress ip = pl.IPAddress;
			foreach (Player p in Players) {
				if (p.Name == name || p.Name == "x." + name) {
					found += 3;
				} else if (ip.Equals (p.IPAddress)) {
					found += 2;
				}
			}
			if (found > 6)
				return "You have reached the limits of accounts in this world.";

			// Increase plays counter
			if (!isGuest) {
				bool wasIn = false;
				for (int i = 0; i < usernames.Length; i++) {
					if (usernames[i] == null) continue;
					if (usernames[i].Name == pl.Name) {
						wasIn = true;
						break;
					}
				}
				if (!wasIn) {
					W_plays++;
					RoomData["plays"] = W_plays.ToString ();
					RoomData.Save ();
				}
			}

			if (usernames.Length < pl.Id + 1)
				Array.Resize (ref usernames, pl.Id + 10);

			usernames[pl.Id] = new PlayerHistory ();
			usernames[pl.Id].Name = pl.Name;
			usernames[pl.Id].Id = pl.ConnectUserId;
			usernames[pl.Id].join_time = getMTime ();
			if (W_verbose) Broadcast ("write", "* VERBOSE", pl.Name + " has joined.");
			return null;
		}

		public override void UserLeft (Player pl) {
			if (W_crown == pl.Id) W_crown = -1;
			if (!pl.isInited)
				return;

			pl.isInited = false;
			Broadcast ("left", pl.Id);
			if (W_verbose) Broadcast ("write", "* VERBOSE", pl.Name + " has left.");
#if !INDEV
			if (pl.Face != 31) pl.GetPlayerObject (delegate (DatabaseObject obj) {
				obj.Set ("face", pl.Face);
				obj.Save ();
			});
#endif
		}

		public override void GotMessage (Player pl, Message m) {
			if (m.Type == "init" ||
				m.Type == "botinit" ||
				m.Type == "access") {

				MainGameFunc (pl, m);
				return;
			}

			if (m.Type == W_rot13 ||
				m.Type == "cb" ||
				m.Type == "cp" ||
				m.Type == "th" ||
				m.Type == "complete" ||
				m.Type == "rcoins" ||
				m.Type == "diamondtouch") {

				if (!W_isLoading && pl.isInited && !W_resized && !pl.isDead) {
					HandleBlock (pl, m);
				}
				return;
			}

			if (m.Type == "say" ||
				m.Type == "m") {

				if (pl.isInited && !W_resized) {
					PlayerInteract (pl, m);
				}
				return;
			}

			if (m.Type == "key" ||
				m.Type == "name" ||
				m.Type == "clear" ||
				m.Type == "save") {

				if (pl.isAdmin && !W_isLoading) {
					OwnerInteract (pl, m);
				}
				return;
			}

			// Check for W_rot13 combinations
			bool isCmd = false;
			char[] ext = { 'f', 'k', 'r', 'g', 'b' };
			for (int i = 0; i < ext.Length; i++) {
				if (m.Type == W_rot13 + ext[i]) {
					isCmd = true;
					break;
				}
			}
			if (m.Type == "god" ||
				m.Type == "mod" ||
				m.Type == "c" ||
				isCmd) {
				if (pl.isInited) {
					GamePlayFunc (pl, m);
				}
				return;
			}
		}

		void MainGameFunc (Player pl, Message m) {
			#region Init
			if ((m.Type == "init" || m.Type == "botinit") &&
				!pl.isInited &&
				!pl.send_init) {

				// Allow god mode for Moderators in open worlds
				if (W_key == "")
					pl.canEdit = !pl.isModerator;

				if (m.Type == "botinit") {
					pl.isBot = true;
					pl.Name = "x." + pl.Name;

					if (m.Count == 1 && m[0] is bool)
						pl.init_binary = (bool) m[0];
				} else {
					pl.firstFace = true;
				}

				if (W_upgrade) {
					// Send dummy output for updated worlds
					pl.Send ("init", "updateOwner", "updateRoom", "0", "key", pl.Id, 16, 16, "", false, false, 2, 2, false);
					pl.Send ("upgrade");
				} else {
					pl.send_init = true;
				}
				return;
			}
			#endregion

			if (m.Type == "access") {
				if (!(m[0] is string) || !pl.isInited) {
					pl.Disconnect ();
					return;
				}
				if (m.GetString (0) == W_key || pl.isModerator) {
					pl.code_tries = 0;
					pl.canEdit = true;
					pl.Send ("access");
				} else {
					pl.code_tries++;
					if (pl.code_tries > 50)
						pl.Disconnect ();
				}
				return;
			}
		}

		void HandleBlock (Player pl, Message m) {
			#region Block placement
			if (m.Type == W_rot13) {
				#region Verify block data
				if (isEditBlocked || m.Count < 4 || !pl.canEdit)
					return;

				for (uint i = 0; i < 4; i++) {
					if (!(m[i] is int))
						return;
				}
				int l = m.GetInt (0),
					x = m.GetInt (1),
					y = m.GetInt (2),
					b = m.GetInt (3);

				if (!isValidCoor (x, y) || (l != 0 && l != 1) || b < 0)
					return;

				Bindex bl = new Bindex (blocks[x, y]);
				#endregion

				#region Get block info
				if (pl.getBlockInfo) {
					string text = "Id: " + (l == 0 ? bl.FG : bl.BG),
						blPlacer = "?";
					if (l == 0) {
						if (bl.FG == 242) {
							text += "\nRotation: " + bl.arg3;
							text += "\nPortal-Id: " + bl.arg4;
							text += "\nTarget portal: " + bl.arg5;
						} else if (bl.FG == 43 || bl.FG == 77) {
							text += "\nArg3: " + bl.arg3;
						} else if (bl.FG == 1000) {
							string ktx = "[ERROR]";
							if (bl.arg3 >= 0 && bl.arg3 < modText.Length) {
								if (modText[bl.arg3] != null)
									ktx = modText[bl.arg3];
							}
							text += "\nText: " + ktx;
						}
						if (bl.FGp > 0) {
							if (usernames[bl.FGp] != null)
								blPlacer = usernames[bl.FGp].Name;
						}
					} else {
						if (blocks[x, y].BGp > 0) {
							if (usernames[bl.BGp] != null)
								blPlacer = usernames[bl.BGp].Name;
						}
					}
					text += "\nPlacer: " + blPlacer.ToUpper ();
					pl.Send ("write", SYS, (l == 0 ? "Block" : "Background") + " [" + x.ToString () + '|' + y.ToString () + "]: \n" + text);
					pl.getBlockInfo = false;
					return;
				}
				#endregion

				Message block_msg = null;
				if (getBlockArgCount (b) == 0) {
					if (b == (l == 0 ? bl.FG : bl.BG) || m.Count != 4) return;

					#region normalBlock
					if (l == 0 && b < (int) C.BLOCK_MAX) {
#if INDEV
						removeOldBlock(x, y, bl.FG, bl.arg3);
						if (b != 0) {
							if (Nblock[0, b] == null)
								Nblock[0, b] = new Block();

							Nblock[0, b].Set(x, y);
						}

						bl.FG = b;
						bl.FGp = pl.Id;
						bl.arg3 = 0;
						bl.pId = 0;
						bl.pTarget = 0;
#else
						#region foreground
						bool edit = false;
						if (b >= 0 && b <= 36) edit = true;     // Default
						if (b >= 37 && b <= 42) edit = true;    // Beta
						if (b == 44) edit = true;               // Black
						if (b >= 45 && b <= 49) edit = true;    // Factory
						if (b == 50 || b == 243) edit = true;   // Secrets
						if (b >= 51 && b <= 58) edit = true;    // Glass
						if (b == 59) edit = true;               // Summer 2011
						if (b >= 60 && b <= 67) edit = true;    // Candy
						if (b >= 68 && b <= 69) edit = true;    // Halloween 2011
						if (b >= 70 && b <= 76) edit = true;    // Minerals
						if (b >= 78 && b <= 82) edit = true;    // Christmas 2011
						if (b >= 84 && b <= 89) edit = true;    // Tiles
						if (b == 90) edit = true;               // White basic
						if (b == 91 || b == 92) edit = true;    // Swamp - One way
						if (b >= 93 && b <= 95) edit = true;    // Ice
						if (b >= 96 && b <= 98) edit = true;    // Gothic
						if (b >= 100 && b <= 101) edit = true;  // Coins
																//if (b >= 110 && b <= 111) edit = true;
						if (b >= 400 && b <= 405) edit = true;  // Materials
						if (b >= 406 && b <= 411) edit = true;  // Wall
						if (b >= 412 && b <= 414) edit = true;  // Winter
						if (b >= 415 && b <= 422) edit = true;  // Wood
						if (b >= 423 && b <= 425) edit = true;  // Marble
						if (b == 426 || b == 427) edit = true;  // Granite
						if (b >= 428 && b <= 438) edit = true;  // Extra Blocks
						if (b >= 439 && b <= 446) edit = true;  // Carpet
						if (b >= 447 && b <= 455) edit = true;  // Mario
						if (b >= 456 && b <= 457) edit = true;  // Extra gothic blocks
						if (b >= 458 && b <= 465) edit = true;  // Evolution

						// Decoration
						if (b == 103 && (pl.isAdmin || pl.isModerator)) {
							edit = true; // Codeblock
							if (Nblock[0, b] != null) {
								if (Nblock[0, b].used > 2)
									edit = false;
							}
						}
						if (b == 104) {
							edit = true; // Checkpoint
							if (Nblock[0, b] != null) {
								if (Nblock[0, b].used > 600)
									edit = false;
							}
						}
						if (b == 105) edit = true; // Hazard (Spikes)
						if ((b == 106 || b == 107) && pl.isAdmin) {
							edit = true; // Trophy
							if (Nblock[0, b] != null) {
								if (Nblock[0, b].used >= 1)
									edit = false;
							}
						}
						if (b == 108 || b == 109) edit = true;  // Water
						if (b == 112) edit = true;              // Ladder
						if (b == 113) edit = true;              // Sand (slow)
						if (b == 118) edit = true;              // Swamp-water
						if (b >= 114 && b <= 117) edit = true;  // Boost

						//end special
						if (b == 121) edit = true;              // Invisible
						if (b == 223) edit = true;              // Halloween 2011 Trophy
						if (b == 227) edit = true;              // Candy
						if (b >= 218 && b <= 222) edit = true;  // Christmas 2011
						if (b >= 224 && b <= 226) edit = true;  // Halloween 2011
						if (b >= 228 && b <= 232) edit = true;  // Summer 2011
						if (b >= 233 && b <= 240) edit = true;  // Spring 2011 Grass
						if (b == 241 && pl.isAdmin) {
							edit = true; // Diamond
							if (Nblock[0, b] != null) {
								if (Nblock[0, b].used > 10)
									edit = false;
							}
						}
						if (b >= 244 && b <= 248) edit = true; // New year 2010
						if (b >= 249 && b <= 254) edit = true; // Christmas 2010
						if (b == 255 && (pl.isAdmin || pl.isModerator)) {
							edit = true; // Spawnpoint
							if (Nblock[0, b] != null) {
								if (Nblock[0, b].used > 60)
									edit = false;
							}
						}
						if (b >= 256 && b <= 264) edit = true; // Swamp plants
						if (b >= 265 && b <= 268) edit = true; // Snow and ice
						if (b >= 269 && b <= 273) edit = true; // Gothic
						if (b >= 274 && b <= 280) edit = true; // Prison
						if (b >= 281 && b <= 285) edit = true; // Extra Gothic/Halloween
						if (b == 286) edit = true; // Snowman
						if (b == 287) edit = true; // Kock

						if (!edit)
							return;

						removeOldBlock (x, y, bl.FG, bl.arg3);
						if (b != 0) {
							if (Nblock[0, b] == null)
								Nblock[0, b] = new Block ();

							Nblock[0, b].Set (x, y);
						}

						bl.FG = b;
						bl.FGp = pl.Id;
						bl.arg3 = 0;
						bl.arg4 = 0;
						bl.arg5 = 0;
						#endregion
#endif
					} else if (l == 1 && ((b >= 500 && b - 500 < (int) C.BLOCK_MAX) || b == 0)) {
#if INDEV
						if (bl.BG >= 500)
							if (Nblock[1, bl.BG - 500] != null)
								Nblock[1, bl.BG - 500].Remove(x, y);

						if (b >= 500) {
							if (Nblock[1, b - 500] == null)
								Nblock[1, b - 500] = new Block();

							Nblock[1, b - 500].Set(x, y);
						}
						bl.BG = b;
						bl.BGp = pl.Id;
#else
						#region background
						bool edit = false;
						if (b == 0) edit = true;
						if (b >= 500 && b <= 512) edit = true;  // Basic
						if (b >= 513 && b <= 519) edit = true;  // Checker
						if (b >= 520 && b <= 526) edit = true;  // Dark
						if (b >= 527 && b <= 532) edit = true;  // Pastel
						if (b >= 533 && b <= 538) edit = true;  // Canvas
						if (b >= 539 && b <= 540) edit = true;  // Candy
						if (b >= 541 && b <= 544) edit = true;  // Halloween 2011
						if (b >= 545 && b <= 549) edit = true;  // Wallpaper
						if (b >= 550 && b <= 555) edit = true;  // Tile
						if (b >= 556 && b <= 558) edit = true;  // Ice
						if (b == 559) edit = true;              // Gothic
						if (b >= 560 && b <= 564) edit = true;  // Fancy
						if (b >= 565 && b <= 568) edit = true;  // Green
						if (b >= 569 && b <= 574) edit = true;  // Stone
						if (b >= 575 && b <= 583) edit = true;  // Hexagonal
						if (b == 584) edit = true;              // Extra backgrounds
						if (b >= 585 && b <= 592) edit = true;  // Evolution

						if (!edit) return;

						if (bl.BG != 0) {
							if (Nblock[1, bl.BG - 500] != null)
								Nblock[1, bl.BG - 500].Remove (x, y);
						}

						if (b != 0) {
							if (Nblock[1, b - 500] == null)
								Nblock[1, b - 500] = new Block ();

							Nblock[1, b - 500].Set (x, y);
						}
						bl.BG = b;
						bl.BGp = pl.Id;
						#endregion
#endif
					} else return;
					block_msg = Message.Create ("b", l, x, y, b);
					#endregion
				} else if (b == 1000) {
					#region Text
					if (b == bl.FG || m.Count != 5 || l != 0)
						return;
					if (!pl.isModerator && !pl.isAdmin && !W_allowText)
						return;

					string text = m.GetString (4);
					if (text.Length == 0 || string.IsNullOrWhiteSpace (text))
						return;

					if (text.Length > 150)
						text = text.Remove (150);

					#region Set modText string
					int arg3 = -2,
						free = -2;

					// Fit empty slot
					for (int i = 0; i < modText.Length; i++) {
						if (!string.IsNullOrEmpty (modText[i])) {
							if (modText[i] == text) {
								// Found same text
								arg3 = i;
								break;
							}
						} else if (free < 0) {
							free = i;
						}
					}

					bool isLimit = false;
					if (arg3 < 0) {
						if (free < 0) {
							if (modText.Length < 200) {
								arg3 = modText.Length;
								Array.Resize (ref modText, Math.Min (modText.Length + 50, 200));
								modText[arg3] = text;
							} else isLimit = true;
						} else {
							modText[free] = text;
							arg3 = free;
						}
					}
					#endregion

					if (isLimit) {
						if (pl.system_messages < sys_msg_max) {
							pl.Send ("write", SYS, "Fatal error: Reached text limit");
							pl.system_messages++;
						}
						return;
					}

					removeOldBlock (x, y, bl.FG, bl.arg3);
					int gid = BlockToSPId (b);
					if (Nblock[gid, arg3] == null)
						Nblock[gid, arg3] = new Block ();

					Nblock[gid, arg3].Set (x, y);
					bl.FG = b;
					bl.FGp = pl.Id;
					bl.arg3 = (byte) arg3;
					bl.arg4 = 0;
					bl.arg5 = 0;

					block_msg = Message.Create ("lb", x, y, b, text);
					#endregion
				} else if (b == 43 || b == 77 /*|| b == 83*/) {
					#region Coin doors, Music blocks
					if (m.Count != 5 || l != 0)
						return;

					int arg3 = m.GetInt (4);
					if (b == bl.FG && arg3 == bl.arg3)
						return;

					bool valid = (b == 43) ? pl.isAdmin : true;
					if (arg3 < 0 || arg3 >= 100 || !valid)
						return;

					int bid = BlockToSPId (b);
					removeOldBlock (x, y, bl.FG, bl.arg3);
					if (Nblock[bid, arg3] == null)
						Nblock[bid, arg3] = new Block ();

					Nblock[bid, arg3].Set (x, y);
					bl.FG = b;
					bl.FGp = pl.Id;
					bl.arg3 = (byte) arg3;

					block_msg = Message.Create ((b == 43) ? "bc" : "bs", x, y, b, arg3);
					#endregion
				} else if (b == 242 && (pl.isAdmin || pl.isModerator)) {
					#region Portals
					if (m.Count != 7 || l != 0)
						return;

					int rotation = m.GetInt (4),
						pId = m.GetInt (5),
						pTarget = m.GetInt (6);
					if (pId < 0 || pId >= 100 || pTarget < 0 || pTarget >= 100) return;

					if (rotation >= 4)
						rotation = 0;

					if (countBlock (b) > 200) {
						if (pl.system_messages < sys_msg_max) {
							pl.Send ("write", SYS, "Fatal error: Reached portal limit");
							pl.system_messages++;
						}
						return;
					}

					removeOldBlock (x, y, bl.FG, bl.arg3);
					if (PBlock[rotation, pId, pTarget] == null)
						PBlock[rotation, pId, pTarget] = new Block ();

					PBlock[rotation, pId, pTarget].Set (x, y);
					bl.FG = b;
					bl.FGp = pl.Id;
					bl.arg3 = (byte) rotation;
					bl.arg4 = (byte) pId;
					bl.arg5 = (byte) pTarget;

					block_msg = Message.Create ("pt", x, y, b, rotation, pId, pTarget);
					#endregion
				}

				if (block_msg == null)
					return;
				blocks[x, y] = bl;

				W_gotEdited = true;
				W_Bcount++;
				Message bot_msg = block_msg;
				bot_msg.Add (pl.Id);
				foreach (Player p in Players) {
					if (p.isInited)
						p.Send (p.isBot ? bot_msg : block_msg);
				}
				return;
			}
			#endregion

			#region cb - Codeblock
			if (m.Type == "cb" && !pl.isBot) {
				if (!pl.canEdit && pl.moved > 0 && !pl.god_mode && !pl.mod_mode) {
					if (getBlock (0, m.GetInt (0), m.GetInt (1)) == 103) {
						pl.canEdit = true;
						pl.Send ("access");
					}
				}
				return;
			}
			#endregion

			#region cp - Checkpoint
			if (m.Type == "cp" && !pl.isBot) {
				int x = m.GetInt (0),
					y = m.GetInt (1);
				if ((pl.cPointX != x || pl.cPointY != y) && !pl.god_mode && !pl.mod_mode) {
					if (getBlock (0, x, y) == 104) {
						pl.cPointX = x;
						pl.cPointY = y;
					}
				}
				return;
			}
			#endregion

			if (m.Type == "th" && !pl.isBot) {
				if (!pl.god_mode && !pl.mod_mode && !pl.isDead && !kill_active) {
					pl.isDead = true;
				}
				return;
			}
			#region complete - Trophy
			if (m.Type == "complete") {
				if (!pl.god_mode && !pl.mod_mode && !pl.levelComplete) {
					if (getBlock (0, m.GetInt (0), m.GetInt (1)) == 106) {
						pl.levelComplete = true;
						Broadcast ("write", SYS, pl.Name.ToUpper () + " completed this world!");
						pl.Send ("info", "Congratulations!", "You completed the world:\n" + W_title);
					}
				}
				return;
			}
			#endregion

			#region rcoins - Reset player's coins
			if (m.Type == "rcoins") {
				pl.coins = 0;
				pl.cPointX = -1;
				pl.cPointY = -1;
				parseSpawns ();
				COOR c = get_next_spawn ();
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;
				Broadcast ("tele", true, pl.Id, pl.posX, pl.posY);
				return;
			}
			#endregion

			if (m.Type == "diamondtouch") {
				if (m.Count >= 2 && !pl.god_mode && !pl.mod_mode && pl.Face != 31) {
					if (getBlock (0, m.GetInt (0), m.GetInt (1)) == 241) {
						Broadcast ("face", pl.Id, 31);
						pl.Face = 31;
					}
				}
				return;
			}
		}

		void GamePlayFunc (Player pl, Message m) {
			if (m.Type == "god" && pl.canEdit && m.Count == 1) {
				if (!W_isOpen || pl.isAdmin || pl.isModerator) {
					Broadcast ("god", pl.Id, m.GetBoolean (0));
					pl.god_mode = m.GetBoolean (0);
				}
				return;
			}
			if (m.Type == "mod") {
				if (!hasAccess (pl, Rights.Moderator)) return;
				pl.mod_mode = m.GetBoolean (0);
				if (!pl.canEdit) {
					pl.Send ("access");
					pl.canEdit = true;
				}
				Broadcast ("mod", pl.Id);
				return;
			}

			#region Change face
			if (m.Type == (W_rot13 + "f")) {
				if (pl.firstFace) {
					pl.firstFace = false;
					Broadcast ("face", pl.Id, pl.Face);
					return;
				}

				int f = m.GetInt (0);
				if (f != pl.Face) {
#if INDEV
					if(f >= 0){
#else
					// Disallow unknown smilies
					if (f >= 0 && f != 31 && f <= (int) C.SMILIES) {
#endif
						Broadcast ("face", pl.Id, f);
						pl.Face = f;
					}
				}
				return;
			}
			#endregion

			if (m.Type == (W_rot13 + "k")) {
				if (!pl.god_mode && !pl.mod_mode && !pl.isDead) {
					W_crownC = true;
					W_crown = pl.Id;
				}
				return;
			}

			#region c - Coin
			if (m.Type == "c") {
				if (W_isLoading || m.Count != 3) return;
				if (getBlock (0, m.GetInt (1), m.GetInt (2)) != 100) {
					pl.mWarns += 2;
					return;
				}

				pl.coins = m.GetInt (0);
				if (pl.coins < 0)
					pl.coins = 0;

				pl.gotCoin = true;
				return;
			}
			#endregion

			#region Keys
			for (byte i = 0; i < key_colors.Length; i++) {
				if (m.Type == W_rot13 + key_colors[i][0]) {
					if (keys[i] == 0) {
						keys[i] = 1;
						Broadcast ("hide", key_colors[i]);
					} else if (keys[i] > 4) {
						keys[i] = 1;
					}
				}
			}
			#endregion
		}

		void OwnerInteract (Player pl, Message m) {
			#region key - Change key
			if (m.Type == "key") {
				W_key = m.GetString (0);
				Broadcast ("lostaccess");
				foreach (Player p in Players) {
					if (!p.isAdmin) {
						p.code_tries = 0;
						p.god_mode = false;
						p.canEdit = false;
					} else if (p.god_mode) // Ugly jump fix
						Broadcast ("god", p.Id, true);
				}
				addLog (pl.Name, "Changed code to " + W_key);
				RoomData["needskey"] = "yup";
				RoomData.Save ();
				return;
			}
			#endregion

			#region name - Change title
			if (m.Type == "name") {
				W_title = m.GetString (0);
				if (W_title.Length > 60) {
					W_title = W_title.Remove (60, W_title.Length - 60);
				}
				addLog (pl.Name, "Changed title");
				Broadcast ("updatemeta", W_Owner, W_title, W_plays);
				RoomData["name"] = W_title;
				RoomData.Save ();
				return;
			}
			#endregion

			if (m.Type == "clear" && !W_isLoading) {
				broadcast_clear_world ();
				respawn_players (true);
				addLog (pl.Name, "Cleared world");
				return;
			}
			if (m.Type == "save" && !W_isLoading) {
				if (W_gotEdited && W_can_save) {
					W_can_save = false;
					addLog (pl.Name, "Saved world");
					save_worlddata (pl);
				}
				// Prevent from mass-save
				W_gotEdited = false;
				return;
			}
		}

		void PlayerInteract (Player pl, Message m) {
			if (m.Type == "say") {
				string msg = m.GetString (0);
				if (msg.Length == 0) return;

				if (msg[0] == '/') {
					#region header
					string[] args = msg.Split (' ');
					int length = 0;

					for (int i = 0; i < args.Length; i++) {
						if (i != length)
							args[length] = args[i];

						if (args[i] != "")
							length++;
					}

					if (args.Length < 10)
						Array.Resize (ref args, 10);

					for (int i = length; i < args.Length; i++)
						args[i] = "";
					#endregion

					#region /commands
					if (args[0] == "/reset") {
						if (!hasAccess (pl, Rights.Admin)) return;
						if (!W_isLoading) {
							respawn_players (true);
							addLog (pl.Name, "Reset players");
						}
						return;
					}
					if (args[0] == "/clear") {
						if (!hasAccess (pl, Rights.Admin)) return;
						if (!W_isLoading) {
							broadcast_clear_world ();
							respawn_players (true);
							addLog (pl.Name, "Cleared world");
						}
						return;
					}
					if (args[0] == "/save") {
						if (!hasAccess (pl, Rights.Admin)) return;
						if (W_isOpen)
							pl.Send ("write", SYS, "You can not save open worlds.");
						else if (W_isLoading)
							pl.Send ("write", SYS, "The world is being loaded. Please try again later.");
						else if (!W_gotEdited)
							pl.Send ("write", SYS, "There are no changes to be saved.");
						else {
							addLog (pl.Name, "Saved world");
							save_worlddata (pl);
						}
						return;
					}

					#region resize
					if (args[0] == "/resize_this_world" && length == 1 && !W_isOpen) {
						if (!hasAccess (pl, Rights.Owner)) return;
						#region resize1
						PlayerIO.BigDB.Load ("Worlds", RoomId, delegate (DatabaseObject w_obj) {
							PlayerIO.BigDB.Load ("PlayerObjects", w_obj.GetString ("owner"), delegate (DatabaseObject o) {
								string[] types = o.GetString ("roomType").Split (','),
									ids = o.GetString ("roomId").Split (',');

								for (int i = 0; i < ids.Length; i++) {
									if (ids[i] == RoomId) {
										string typestring = types[i];
										if (typestring == "beta0" || typestring == "beta1") {
											W_type = 3;
										} else W_type = info.getInt (typestring.Split ('x')[0]);
										break;
									}
								}
								if (W_type < 0) {
									pl.Send ("write", "* RESIZER", "Something strange happened, contact a moderator please.");
									return;
								}
								int[] newSize = info.getWorldSize (W_type);
								int diff_x = W_width - newSize[0],
									diff_y = W_height - newSize[1];
								if (diff_x == 0 && diff_y == 0) {
									W_type = -1;
									pl.Send ("write", "* RESIZER", "Not required to resize this world.");
									return;
								}
								pl.Send ("write", "* RESIZER", "Please note: With this action, you can destroy parts of your world!");
								pl.Send ("write", "* RESIZER", "Old size: " + W_width + "x" + W_height);
								pl.Send ("write", "* RESIZER", "New size: " + newSize[0] + "x" + newSize[1] + " (Diff: " + diff_x + "x" + diff_y + ")");
								pl.Send ("write", "* RESIZER", "Say '" + args[0] + " " + W_rot13 + "' to resize this world.");
							});
						});
						return;
						#endregion
					}
					if (args[0] == "/resize_this_world" && length > 1) {
						if (!hasAccess (pl, Rights.Owner)) return;
						#region resize2
						if (W_type < 0) {
							pl.Send ("write", "* RESIZER", "Say '" + args[0] + "' to see what changes.");
							return;
						}
						if (args[1] != W_rot13) {
							pl.Send ("write", "* RESIZER", "Invalid argument. STOP.");
							return;
						}

						int[] newSize = info.getWorldSize (W_type);
						if ((W_width - newSize[0]) == 0 && (W_height - newSize[1]) == 0) {
							pl.Send ("write", "* RESIZER", "Not required to resize this world.");
							return;
						}

						PlayerIO.BigDB.Load ("Worlds", RoomId, delegate (DatabaseObject w_obj) {
							W_width = newSize[0];
							W_height = newSize[1];
							w_obj.Set ("width", W_width);
							w_obj.Set ("height", W_height);
							w_obj.Save ();
							save_worlddata (pl, true);
						});
						#endregion
						return;
					}
					#endregion
					if (args[0] == "/kick") {
						if (!hasAccess (pl, Rights.Vigilant, length > 1)) return;
						#region kick
						args[1] = args[1].ToLower ();
						bool found = false;
						string content = "Tsk. Tsk.";
						if (length > 2) {
							content = "";
							for (int i = 2; i < length; i++) {
								content += args[i] + " ";
							}
						}

						Rights rights = get_rights (pl);
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (get_rights (p) >= rights) break;

								found = true;
								p.Send ("info", "You got kicked by " + pl.Name, content);
								p.Disconnect ();
							}
						}
						if (found) {
							Broadcast ("write", SYS, pl.Name + " kicked " + args[1].ToUpper () + ": " + content);
						} else pl.Send ("write", SYS, "Unknown username or player is the owner or a moderator");
						#endregion
						return;
					}
					if (args[0] == "/giveedit") {
						if (!hasAccess (pl, Rights.Admin, length >= 2)) return;
						#region giveedit
						bool found = false;
						args[1] = args[1].ToLower ();
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.canEdit) {
									found = true;
									p.canEdit = true;
									p.Send ("access");
									p.Send ("write", SYS, "You can now edit this world.");
								}
							}
						}
						if (found) {
							addLog (pl.Name, "[+] edit: " + args[1].ToUpper ());
							pl.Send ("write", SYS, args[1].ToUpper () + " can now edit this world");
						} else pl.Send ("write", SYS, "Unknown username or player already has edit");
						#endregion
						return;
					}
					if (args[0] == "/removeedit") {
						if (!hasAccess (pl, Rights.Admin, length > 1)) return;
						#region removeedit
						args[1] = args[1].ToLower ();
						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.isAdmin && p.canEdit) {
									found = true;
									p.canEdit = false;
									p.god_mode = false;
									Broadcast ("god", p.Id, false);
									p.Send ("lostaccess");
									p.Send ("write", SYS, "You can no longer edit this world.");
								}
							}
						}
						if (found) {
							addLog (pl.Name, "[-] edit: " + args[1].ToUpper ());
							pl.Send ("write", SYS, args[1].ToUpper () + " can no longer edit this world");
						} else pl.Send ("write", SYS, "Unknown username, player is owner or does not have edit.");
						#endregion
						return;
					}
					if (args[0] == "/kill") {
						if (!hasAccess (pl, Rights.Admin, length > 1)) return;
						#region kill player
						bool found = false;
						args[1] = args[1].ToLower ();
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.god_mode && !p.mod_mode) {
									p.isDead = true;
									found = true;
								}
							}
						}
						if (!found) pl.Send ("write", SYS, "Unknown username or player is god/mod");
						#endregion
						return;
					}
					if (args[0] == "/ban") {
						if (!hasAccess (pl, Rights.Vigilant, length > 1)) return;
						#region banning
						string player_name = args[1].ToLower ();
						bool found = false,
							isGuest = false;
						if (player_name.StartsWith ("x."))
							player_name = player_name.Remove (0, 2);

						if (player_name.StartsWith ("guest-")) {
							player_name = "guest";
							isGuest = true;
						}

						Rights rights = get_rights (pl);
						foreach (Player p in Players) {
							if ((p.Name == player_name ||
									p.Name == "x." + player_name ||
									(isGuest && p.isGuest)
								) && get_rights (p) < rights) {
								p.Send ("info", "Banned", "You have been banned from this world.");
								p.Disconnect ();
								found = true;
							}
						}
						if (found) {
							banned.Add (player_name);
							Broadcast ("write", SYS, pl.Name + " banned " + player_name);
						} else {
							pl.Send ("write", SYS, "Unknown username, player is owner or moderator");
						}
						#endregion
						return;
					}
					if (args[0] == "/cmban") {
						if (!pl.isVigilant && !pl.isModerator) return;
						if (length != 3) {
							pl.Send ("write", SYS, "Please use " + args[0] + " <player> <hours>");
							return;
						}
						#region banning from EE CM
						string player_name = args[1].ToLower ();
						bool found = false;

						float ban_time = 0;
						float.TryParse (args[2], out ban_time);

						if (ban_time < 0.1 || ban_time > 72) {
							pl.Send ("write", SYS, "Please choose a value of hours between 0.1 and 72.");
							return;
						}

						string userId = "";
						foreach (Player p in Players) {
							if (!p.isAdmin && !p.isVigilant && !p.isModerator) {
								p.Send ("info", "Banned", "This account has been banned from EE CM." +
									"Please wait " + ban_time +
									" hour(s) until your ban expires.");
								p.Disconnect ();
								userId = p.ConnectUserId;
								found = true;
							}
						}

						if (found) {
							long time_now = getMTime ();
							PlayerIO.BigDB.Load ("PlayerObjects", userId, delegate (DatabaseObject o) {
								if (o.Contains ("banned") && o["banned"] is bool) {
									o.Remove ("banned");
								}
								o.Set ("banned", time_now + (long) (ban_time * 3600));
								o.Save ();
							});
							Broadcast ("write", SYS, pl.Name + " banned " + player_name + " from EE CM");
						} else {
							pl.Send ("write", SYS, "Unknown username, player is owner, vigilant or moderator");
						}
						#endregion
						return;
					}
					if (args[0] == "/unban") {
						if (!hasAccess (pl, Rights.Vigilant, length > 1)) return;
						#region unbanning
						args[1] = args[1].ToLower ();
						if (banned.Contains (args[1])) {
							banned.Remove (args[1]);
							Broadcast ("write", SYS, pl.Name + " unbanned " + args[1]);
						} else pl.Send ("write", SYS, "This player is not banned.");
						#endregion
						return;
					}
					if (args[0] == "/list") {
						if (!hasAccess (pl, Rights.Normal, length > 1)) return;
						#region list
						args[1] = args[1].ToLower ();
						string list = "";
						if (args[1] == "ban" || args[1] == "bans") {
							if (!hasAccess (pl, Rights.Vigilant)) return;
							string[] banned_array = banned.GetData ();
							for (int i = 0; i < banned_array.Length; i++) {
								list += banned_array[i] + ", ";
							}
							pl.Send ("write", SYS, "List of banned users: " + list);
						} else if (args[1] == "admin" || args[1] == "admins") {
							string[] admins_array = admins.GetData ();
							for (int i = 0; i < admins_array.Length; i++) {
								list += admins_array[i] + ", ";
							}
							pl.Send ("write", SYS, "List of admins: " + list);
						} else if (args[1] == "mute" || args[1] == "mutes") {
							string[] muted_array = pl.muted.GetData ();
							for (int i = 0; i < muted_array.Length; i++) {
								list += muted_array[i] + ", ";
							}
							pl.Send ("write", SYS, "All players on your mute list: " + list);
						} else pl.Send ("write", SYS, "Unknown argument. Use either ban(s), admin(s) or mute(s).");
						#endregion
						return;
					}
					if (args[0] == "/addadmin") {
						if (!hasAccess (pl, Rights.Owner, length > 1)) return;
						#region addadmin
						bool found = false;
						args[1] = args[1].ToLower ();
						if (admins.Contains (args[1]) ||
								args[1] == W_Owner || args[1] == "x." + W_Owner) {
							pl.Send ("write", SYS, "Player '" + args[1].ToUpper () + "' is already an admin.");
							return;
						}

						foreach (Player p in Players) {
							if (p.Name == args[1] && !p.isAdmin) {
								found = true;
								p.Send ("write", SYS, "You are now an admin of this world. Please rejoin.");
							}
						}
						if (found) {
							pl.Send ("write", SYS, args[1].ToUpper () + " is now an admin.");
							admins.Add (args[1]);
						} else {
							PlayerIO.BigDB.Load ("Usernames", args[1], delegate (DatabaseObject obj) {
								if (obj.ExistsInDatabase) {
									pl.Send ("write", SYS, args[1].ToUpper () + " is now an admin.");
									admins.Add (args[1]);
								} else {
									pl.Send ("write", SYS, "Unknown username");
								}
							});
						}
						#endregion
						return;
					}
					if (args[0] == "/rmadmin") {
						if (!hasAccess (pl, Rights.Owner, length > 1)) return;
						#region rmadmin
						args[1] = args[1].ToLower ();

						if (admins.Contains (args[1])) {
							admins.Remove (args[1]);
							foreach (Player p in Players) {
								if (p.Name == args[1]) {
									p.isAdmin = false;
									p.Send ("write", SYS, "Your admin rank was removed.");
								}
							}
							pl.Send ("write", SYS, args[1].ToUpper () + " is no longer an admin.");
						} else {
							pl.Send ("write", SYS, "Unknown username or player is not an admin");
						}
						#endregion
						return;
					}
					if (args[0] == "/teleport" || args[0] == "/tp") {
						if (!hasAccess (pl, Rights.Edit, length > 1)) return;
						if (W_isOpen && !pl.isModerator && !pl.isAdmin) {
							pl.Send ("write", SYS, "You can not teleport in an open world.");
							return;
						}
						#region stalking
						string src = pl.Name,
							dst = "";
						int x = 0,
							y = 0;
						if (length >= 4) {
							// /teleport name X Y
							src = args[1].ToLower ();
							x = info.getInt (args[2]);
							y = info.getInt (args[3]);
							if (!hasAccess (pl, Rights.None, isValidCoor (x, y))) return;
						} else if (length == 3) {
							//  /teleport X Y
							x = info.getInt (args[1]);
							y = info.getInt (args[2]);
							if (!isValidCoor (x, y)) {
								// /teleport name name
								src = args[1].ToLower ();
								dst = args[2].ToLower ();
							}
						} else {
							dst = args[1].ToLower ();
						}

						if (src != pl.Name) {
							if (!hasAccess (pl, Rights.Admin)) return;
						}

						if (dst == "") {
							x *= 16;
							y *= 16;
						} else {
							foreach (Player p in Players) {
								if (p.Name == dst) {
									x = p.posX;
									y = p.posY;
									dst = "";
									break;
								}
							}
							if (dst != "") {
								pl.Send ("write", SYS, "Could not find player '" + dst.ToUpper () + "'");
								return;
							}
						}

						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == src) {
								Broadcast ("tele", false, p.Id, x, y);
								p.posX = x;
								p.posY = y;
								found = true;
							}
						}

						if (!found) {
							pl.Send ("write", SYS, "Could not find player '" + src.ToUpper () + "'");
						}
						#endregion
						return;
					}
					if (args[0] == "/loadlevel" || args[0] == "/load") {
						if (!hasAccess (pl, Rights.Admin)) return;
						if (W_isSaved && !W_isLoading) {
							addLog (pl.Name, "Loaded world");
							load_worlddata (true);
							foreach (Player p in Players) {
								p.coins = 0;
							}
						}
						return;
					}
					if (args[0] == "/getblockinfo" || args[0] == "/gbi") {
						if (!hasAccess (pl, Rights.Edit)) return;
						pl.getBlockInfo = true;
						pl.Send ("write", SYS, "Now, click on the block from which you want the information about.");
						return;
					}
					#region modpower
					if (args[0] == "/info") {
						if (!hasAccess (pl, Rights.Moderator, length > 2)) return;
						string content = "";
						for (int i = 2; i < length; i++) {
							content += args[i] + " ";
						}
						Broadcast ("info", "Moderator Message: " + args[1], content);
						return;
					}
					if (args[0] == "/write") {
						if (!hasAccess (pl, Rights.Moderator, length > 2)) return;
						#region modwrite
						args[1] = args[1].ToLower ();
						string content = "";
						for (int i = 2; i < length; i++) {
							content += args[i] + " ";
						}
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								args[1] = SYS;
								//Real player, add sign
								break;
							}
						}
						Broadcast ("write", args[1], content);
						#endregion
						return;
					}
					if (args[0] == "/code") {
						if (!hasAccess (pl, Rights.Admin, length > 1)) return;
						OwnerInteract (pl, Message.Create ("key", args[1]));
						return;
					}
					if (args[0] == "/name") {
						if (!hasAccess (pl, Rights.Admin, length > 1)) return;
						#region name
						string content = "";
						for (int i = 1; i < length; i++) {
							content += args[i] + " ";
						}
						OwnerInteract (pl, Message.Create ("name", content));
						#endregion
						return;
					}
					if (args[0] == "/getip") {
						if (!hasAccess (pl, Rights.Moderator, length > 1)) return;
						#region getIP
						bool found = false;
						args[1] = args[1].ToLower ();
						foreach (Player p in Players) {
							if (p.Name == args[1] && !p.isAdmin) {
								found = true;
								pl.Send ("write", SYS, args[1] + "'s IP: " + p.IPAddress.ToString ());
								break;
							}
						}
						if (!found) pl.Send ("write", SYS, "Unknown username");
						#endregion
						return;
					}
					if (args[0] == "/eliminate") {
						if (!hasAccess (pl, Rights.Moderator, length > 1)) return;
						#region kick player
						args[1] = args[1].ToLower ();
						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								p.Disconnect ();
								found = true;
							}
						}
						if (!found) {
							pl.Send ("write", SYS, "Unknown username");
						}
						#endregion
						return;
					}
					#endregion
					if (args[0] == "/killroom") {
						if (!hasAccess (pl, Rights.Owner)) return;
						Broadcast ("info", "World Killed", "This world has been killed by " + (pl.isAdmin ? "the owner" : " a moderator"));
						foreach (Player p in Players) {
							p.Disconnect ();
						}
						foreach (Player p in Players) {
							p.Disconnect ();
						}
						return;
					}
					if (args[0] == "/respawn") {
						#region Respawn player
						if (!W_canRespawn && get_rights (pl) < Rights.Admin) {
							pl.Send ("write", SYS, "The respawn privilege is deactivated. You can not respawn.");
							return;
						}
						string name = "";
						if (length > 1) {
							if (!hasAccess (pl, Rights.Admin)) return;

							name = args[1].ToLower ();
						} else {
							name = pl.Name;
						}

						bool found = false;
						parseSpawns ();
						foreach (Player p in Players) {
							if (p.Name != name || p.god_mode || p.mod_mode)
								continue;

							found = true;
							p.cPointX = -1;
							p.cPointY = -1;
							COOR c = get_next_spawn ();
							p.posX = c.x * 16;
							p.posY = c.y * 16;
							Broadcast ("tele", false, p.Id, p.posX, p.posY);
						}

						if (!found)
							pl.Send ("write", SYS, "Unknown username or the player is in god/mod mode.");
						#endregion
						return;
					}
					if (args[0] == "/upgrade") {
						if (!hasAccess (pl, Rights.Moderator)) return;
						W_upgrade = true;
						foreach (Player p in Players) {
							if (!p.isModerator) {
								p.Send ("upgrade");
								p.Disconnect ();
							}
						}
						RoomData["name"] = "shit";
						RoomData.Save ();
						return;
					}

					if (args[0] == "/me") {
						if (!hasAccess (pl, Rights.Normal, length > 1)) return;
						#region action
						string content = "";
						for (int i = 1; i < length; i++) {
							content += args[i] + " ";

							if (content.Length > W_chatLimit) {
								content = content.Remove (W_chatLimit);
								break;
							}
						}

						if (content == "")
							return;

						handle_spam (pl, content);

						if (pl.sameText > 4 || pl.say_counter > 3) {
							pl.Send ("write", SYS, "You try to spam, please be nice!");
							return;
						}

						Broadcast ("write", "* WORLD", pl.Name.ToUpper () + " " + content);
						#endregion
						return;
					}
					if (args[0] == "/pm") {
						if (!hasAccess (pl, Rights.Normal, length > 2)) return;
						#region pm
						args[1] = args[1].ToLower ();
						bool found = false;
						string content = "";
						for (int i = 2; i < length; i++) {
							content += args[i] + " ";

							if (content.Length > W_chatLimit) {
								content = content.Remove (W_chatLimit);
								break;
							}
						}

						if (content == "")
							return;

						handle_spam (pl, content);

						if (pl.sameText > 4 || pl.say_counter > 3) {
							pl.Send ("write", SYS, "You try to spam, please be nice!");
							return;
						}

						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								found = true;
								if (!p.muted.Contains (pl.Name))
									p.Send ("write", "*FROM " + pl.Name.ToUpper (), content);
							}
						}

						if (found)
							pl.Send ("write", "*TO " + args[1].ToUpper (), content);
						else
							pl.Send ("write", SYS, "Unknown username");
						#endregion
						return;
					}
					if (args[0] == "/mute") {
						if (!hasAccess (pl, Rights.Normal, length > 1)) return;
						#region mute
						string name = args[1].ToLower ();
						if (pl.muted.Contains (name)) {
							pl.Send ("write", SYS, "This player is already muted.");
							return;
						}

						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == name) {
								found = true;
							}
						}
						if (found) {
							pl.muted.Add (name);
							pl.Send ("write", SYS, "All further messages from " + name.ToUpper () + " will be hidden from you.");
						} else {
							pl.Send ("write", SYS, "Unknown username");
						}
						#endregion
						return;
					}
					if (args[0] == "/unmute") {
						if (!hasAccess (pl, Rights.Normal, length > 1)) return;
						#region unmute
						args[1] = args[1].ToLower ();
						if (!pl.muted.Contains (args[1])) {
							pl.Send ("write", SYS, "This player is not muted.");
							return;
						}
						pl.muted.Remove (args[1]);
						pl.Send ("write", SYS, "The messages from " + args[1].ToUpper () + " will be visible for you again.");
						#endregion
						return;
					}
					if (args[0] == "/woot") {
						if (!pl.wootGiven) {
							Broadcast ("write", SYS, pl.Name.ToUpper () + " gave a woot");
							pl.wootGiven = true;
						} else pl.Send ("write", SYS, "You already wooted for this world.");
						return;
					}
					if (args[0] == "/log") {
						if (!hasAccess (pl, Rights.Admin)) return;
						string txt = "World Log: (newest on top)";
						for (int i = 0; i < logbook.Length; i++) {
							txt += '\n' + (i + 1).ToString () + ". " + logbook[i];
						}
						pl.Send ("write", SYS, txt);
						return;
					}
					if (args[0] == "/rankof") {
						if (!hasAccess (pl, Rights.Normal, length > 1)) return;

						args[1] = args[1].ToLower ();

						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								pl.Send ("write", SYS, "Rank of player " + args[1].ToUpper () + ": " + get_rights (p).ToString ());
								return;
							}
						}
						pl.Send ("write", SYS, "Unknown username.");
						return;
					}
					if (args[0] == "/set") {
						if (!hasAccess (pl, Rights.Admin, length > 2))
							return;
						#region Change a boolean world setting

						if (args[1].ToLower () == "help") {
							string ret = "Unknown setting `" + args[2].ToLower () + "´. See `help all´";
							switch (args[2].ToLower ()) {
								case "all":
									ret = "respawn, text, save_experimental";
									break;
								case "respawn":
									ret = "The permission for regular players to use the /respawn command.";
									break;
								case "text":
								case "texts":
									ret = "The permission to place text blocks in the world.";
									break;
								case "save_experimental":
									ret = "Feature to save the world as a (up to) 10 times file. [Owner rank]";
									break;
								case "verbose":
									ret = "Toggles join/leave messages.";
									break;
							}

							pl.Send ("write", SYS, ret);
							return;
						}

						bool newValue = is_yes (args[2]);

						switch (args[1].ToLower ()) {
							case "respawn":
								set_setting (pl, newValue, ref W_canRespawn, "Respawn privilege");
								break;
							case "text":
							case "texts":
								set_setting (pl, newValue, ref W_allowText, "Text blocks");
								break;
							case "save_experimental":
								if (!hasAccess (pl, Rights.Owner))
									return;

								set_setting (pl, newValue, ref W_experimental_saving, "Experimental DB saving");
								break;
							case "verbose":
								set_setting (pl, newValue, ref W_verbose, "Verbose");
								break;
							default:
								pl.Send ("write", SYS, "Unknown setting `" + args[1].ToLower () + "´. See `help all´");
								break;
						}
						#endregion;
						return;
					}
					if (args[0] == "/help" && length == 1) {
						#region Output this huge commands list
						Rights level = get_rights (pl);
						string lMgr = "Level Managing: /getblockinfo, /gbi" + (W_isSaved ? ", /list admins" : ""),
							pSpec = "\n\nPlayer specific: /respawn, /woot, /rankof [name], /mute [name], /unmute [name], /list mutes",
							cTool = "\n\nOther tools: /pm [name] [text], /teleport {[name], [x] [y]}, /me [text]";
						if (level >= Rights.Vigilant) {
							lMgr += ", /ban [name], /unban [name], /list bans";
							pSpec += ", /kick [name] [reason]";
						}

						if (level >= Rights.Admin) {
							lMgr += ", /clear, /reset, /log, /set help all, (/load) /loadlevel, /code [text], /name [text]";
							pSpec += ", /kill [name], /giveedit [name], /removeedit [name], /respawn [name]";
							cTool += ", (/tp) /teleport [name] {[to_name], [x] [y]}";
						}
						if (level >= Rights.Owner) {
							lMgr += ", /resize_this_world, /killroom" + (W_isSaved ? ", /addadmin [name], /rmadmin [name]" : "");
						}
						if (level == Rights.Moderator) {
							pSpec += ", /getip [name]";
							cTool += ", /write [name] [text], /info [title] [text]";
						}
						pl.Send ("write", SYS, lMgr + pSpec + cTool + "\n\nSee /help [command] for further information.");
						#endregion
						return;
					}
					if (args[0] == "/help" && length > 1) {
						#region detailed help
						string cmd = args[1].ToLower ();
						if (cmd.StartsWith ("/"))
							cmd = cmd.Remove (0, 1);

						string ret = "There are no information for the command `" + cmd + "´.";
						switch (cmd) {
							case "ban":
								ret = "Bans a player until world closes, that player can not join the world anymore. [Admin rank]";
								break;
							case "unban":
								ret = "Unbans a banned player. [Admin rank]";
								break;
							case "giveedit":
								ret = "Gives edit to a player. [Admin rank]";
								break;
							case "removeedit":
								ret = "Removes edit from a player. [Admin rank]";
								break;
							case "mute":
								ret = "Mutes a player.";
								break;
							case "unmute":
								ret = "Removes a player from your mute list.";
								break;
							case "admins":
								ret = "Admins can save, load and clear, they also can access to normal world owner commands.";
								break;
							case "addadmin":
								ret = "Adds a player to the list of admins. [Owner rank]";
								break;
							case "rmadmin":
								ret = "Removes a player from the list of admins. [Owner rank]";
								break;

							case "clear":
								ret = "Clears the current world. [Admin rank]";
								break;
							case "code":
								ret = "Changes code without logbook entry. [Admin rank]";
								break;
							case "getblockinfo":
							case "gbi":
								ret = "Gets the block information from the one you click on. [Edit needed]";
								break;
							case "getip":
								ret = "Gets the IP of the given player. [Moderator rank]";
								break;
							case "info":
								ret = "Shows up an info box. [Moderator rank]";
								break;
							case "kick":
								ret = "Kicks a player from the world. Use /ban to keep them off. [Vigilant/Admin rank]";
								break;
							case "kill":
								ret = "Kills a player - Player gets teleported to the last checkpoint or spawn. [Admin rank]";
								break;
							case "killroom":
								ret = "Kills the world. [Owner rank]";
								break;
							case "list":
								ret = "Returns you a specific list. Available lists: ban, admin, mute";
								break;
							case "loadlevel":
							case "load":
								ret = "Loads the saved world. [Admin rank]";
								break;
							case "log":
								ret = "Returns you a detailed logbook of the world-changes with up to 5 entries.";
								break;
							case "me":
								ret = "Outputs a message from 3rd person perspective.";
								break;
							case "name":
								ret = "Changes the world name without logbook entry. [Admin rank]";
								break;
							case "pm":
								ret = "Sends a private message to a player.";
								break;
							case "rankof":
								ret = "Gets the rank of a player.";
								break;
							case "reset":
								ret = "Resets all players to the spawn points. [Admin rank]";
								break;
							case "resize_this_world":
								ret = "Resizes the current world. [Owner rank]";
								break;
							case "respawn":
								ret = "Respawns you or another player to the spawn point.";
								break;
							case "save":
								ret = "Saves the world. [Admin rank]";
								break;
							case "set":
								ret = "Sets a boolean world setting. See `/set help all´. [Admin rank]";
								break;
							case "teleport":
							case "tp":
								ret = "Possible arguments for teleport: /teleport (or /tp) \n";
								ret += " [to_name] - Teleports you to a player.\n";
								ret += " [name] [to_name] - Teleports a player to another.\n";
								ret += " [x] [y] - Teleports you to the given coordinates.";
								break;
							case "write":
								ret = "Writes as a defined player in the world a message. [Moderator rank]";
								break;
							case "woot":
								ret = "Gives a woot to the world, they do not save.";
								break;
						}
						pl.Send ("write", SYS, ret);
						#endregion
						return;
					}
					pl.Send ("write", SYS, "Unknown command. See /help for all commands.");
					#endregion
					return;
				}
				if (pl.isGuest)
					return;

				#region Spamfilter
				if (msg.Length > W_chatLimit)
					msg = msg.Remove (W_chatLimit, msg.Length - W_chatLimit);

				handle_spam (pl, msg);

				if (pl.sameText > 6) {
					pl.Send ("write", SYS, "Please bring up something new. Your messages seem to repeat");
					return;
				}

				if (pl.say_counter > 3) {
					pl.Send ("write", SYS, "You try to spam, please be nice!");
					return;
				}

				bool move_log = true;
				for (int i = 0; i < oldChat0.Length; i++) {
					if (string.IsNullOrEmpty (oldChat0[i])) {
						oldChat0[i] = pl.Name;
						oldChat1[i] = msg;
						move_log = false;
						break;
					}
				}
				if (move_log) {
					for (int i = 0; i < oldChat0.Length - 1; i++) {
						oldChat0[i] = oldChat0[i + 1];
						oldChat1[i] = oldChat1[i + 1];
					}
					oldChat0[oldChat0.Length - 1] = pl.Name;
					oldChat1[oldChat0.Length - 1] = msg;
				}

				foreach (Player p in Players) {
					if (!p.isGuest && !p.muted.Contains (pl.Name))
						p.Send ("say", pl.Id, msg);
				}
				#endregion
				return;
			}
			if (m.Type == "m") {
				#region Movements
				if (m.Count < 8) {
					pl.Disconnect ();
					return;
				}

				pl.moved++;
				pl.posX = m.GetInt (0);
				pl.posY = m.GetInt (1);
				pl.speedX = m.GetInt (2);
				pl.speedY = m.GetInt (3);

				int gravityX = m.GetInt (4), //gravity
					gravityY = m.GetInt (5),
					keyX = m.GetInt (6), //key press
					keyY = m.GetInt (7);
				pl.gravityX = gravityX;
				pl.gravityY = gravityY;
				pl.keyX = keyX;
				pl.keyY = keyY;

				int x = (int) Math.Round (pl.posX / 16.0),
					y = (int) Math.Round (pl.posY / 16.0);
				if (!isValidCoor (x, y)) {
					pl.Disconnect ();
					return;
				}

				if (W_isLoading || pl.moved > moveLimit)
					return;

				if (pl.mWarns >= 25 && !pl.isModerator) {
					pl.Send ("info", "Error", "The data from your client is not correct.");
					pl.Disconnect ();
					return;
				} else if (pl.mWarns > -10) {
					pl.mWarns--;
				}

				bool skip_send = false,
					has_gravity = !pl.god_mode && !pl.mod_mode && !pl.isAdmin;

				#region anti-cheat
				if (has_gravity) {
					bool valid = false;
					for (sbyte mY = -1; mY <= 1; mY++) {
						for (sbyte mX = -1; mX <= 1; mX++) {
							int bl = getBlock (0, mX + x, mY + y),
								gX = keyX,
								gY = 2;
							byte dir = 1; // 0 = none; 1 = updown; 2 = leftright;
							bool is_liquid = false;

							switch (bl) {
								case 1:
									gX = -2;
									gY = keyY;
									dir = 2;
									break;
								case 2:
									gY = -2;
									break;
								case 3:
									gX = 2;
									gY = keyY;
									dir = 2;
									break;
								default:
									if (Contains (gravity0Id, bl)) {
										gY = pl.keyY;
										dir = 0;
									} else if (bl == 108 || bl == 109) {
										gY = (pl.keyY == -1) ? -2 : 0;
										dir = 0;
										is_liquid = true;
									}
									break;
							}
							if (gX == gravityX && gY == gravityY) {
								if (!is_liquid)
									skip_send = (((dir == 1 && keyX == 0) ||
										(dir == 2 && keyY == 0)) &&
										pl.speedX == 0 &&
										pl.speedY == 0);
								valid = true;
								break;
							}
						}
						if (valid)
							break;
					}
					if (!valid)
						pl.mWarns += 8;
				}

				foreach (Player p in Players) {
					if (p.Id == pl.Id || (skip_send && !p.isBot))
						continue;
					p.Send ("m", pl.Id, pl.posX, pl.posY, pl.speedX, pl.speedY,
								gravityX, gravityY, keyX, keyY);
				}

				if (pl.speedY < -60 && has_gravity) { //-53
					bool isBoost = false;
					for (byte mY = 0; mY <= 20; mY++) {
						for (sbyte mX = -10; mX <= 10; mX++) {
							if (getBlock (0, x + mX, y + mY) == 116) {
								isBoost = true;
								break;
							}
						}
						if (isBoost)
							break;
					}
					if (!isBoost)
						pl.mWarns += 8;
				}
				#endregion
				#endregion
			}
		}

		void Keys_Timer () {
			byte t = 25;
			// 0 = red | 1 = green | 2 = blue

			for (byte i = 0; i < keys.Length; i++) {
				if (keys[i] >= t) {
					Broadcast ("show", key_colors[i]);
					keys[i] = 0;
				} else if (keys[i] > 0)
					keys[i]++;
			}

			if (W_crownC && W_crown != -1) {
				W_crownC = false;
				Broadcast ("k", W_crown);
			}
			isEditBlocked = (W_Bcount >= 60);

			W_Bcount = 0;
		}
		void respawn_players (bool clearCoins) {
			Message msg = Message.Create ("tele", clearCoins);
			parseSpawns ();

			byte players = 0;
			foreach (Player p in Players) {
				if (clearCoins) {
					p.coins = 0;
					p.cPointX = -1;
					p.cPointY = -1;
				}
				if (p.god_mode || p.mod_mode) continue;
				COOR c = get_next_spawn ();

				p.speedX = 0;
				p.speedY = 0;
				p.gravityX = 0;
				p.gravityY = 0;
				p.keyX = 0;
				p.keyY = 0;
				p.posX = c.x * 16;
				p.posY = c.y * 16;
				msg.Add (p.Id, p.posX, p.posY);
				players++;
			}
			if (players > 0) {
				Broadcast (msg);
			}
		}

		#region Database save and load functions
		ushort[] DB_FLAGS = new ushort[] {
			1 << 15, // X
			1 << 14, // Y
			1 << 13, // FG
			1 << 12, // BG
			1 << 11, // arg3
		};
		int DB_FLAGS_MASK = 0;
		System.Text.Encoding enc = System.Text.Encoding.UTF8;

		byte[] serializeData () {
			#region Define variables
			MemoryStream stream = new MemoryStream ();
			BinaryWriter writer = new BinaryWriter (stream);
			writer.Write (0xC0FFEE03);

			SaveEntry last = new SaveEntry (),
				cur = new SaveEntry ();
			#endregion
			writer.Write ((ushort) W_width);

			for (; cur.y < W_height; cur.y++) {
				cur.x = 0;
				for (; cur.x < W_width; cur.x++) {
					Bindex b = blocks[cur.x, cur.y];
					cur.FG = b.FG;
					cur.BG = b.BG;

					if (cur.BG >= 500)
						cur.BG -= 480;

					if (getBlockArgCount (cur.FG) > 0)
						cur.arg3 = b.arg3;

					if (cur.FG == last.FG &&
						cur.BG == last.BG &&
						cur.arg3 == last.arg3) {

						// Continue when no update is required
						continue;
					}

					int header = 0,
						pos = -1;

					if (cur.y == last.y && cur.x - last.x == 1)
						last.x = cur.x;
					if (cur.y == last.y + 1 && cur.x - last.x < 0)
						last.y = cur.y;

					for (int i = 0; i < DB_FLAGS.Length; i++) {
						if (cur[i] == last[i])
							continue;

						// Set modification flag
						header |= DB_FLAGS[i];

						if (pos >= 0)
							continue;

						if (cur[i] >= 2048)
							throw new Exception ("Number overflow, can not send " + cur[i]);

						header |= cur[i];
						pos = i + 1;
					}
					writer.Write ((ushort) header);

					for (; pos < DB_FLAGS.Length; pos++) {
						if (cur[pos] == last[pos])
							continue;

						int value = cur[pos];

						if (pos == 3 || pos == 4) // BG, arg3
							writer.Write ((byte) value);
						else
							writer.Write ((ushort) value);
					}
					last = new SaveEntry (cur);
				}
			}
			writer.Write ((ushort) 0xDEAD);

			#region Portals
			for (int y = 0; y < W_height; y++) {
				for (int x = 0; x < W_width; x++) {
					if (blocks[x, y].FG != 242)
						continue;

					writer.Write (blocks[x, y].arg4);
					writer.Write (blocks[x, y].arg5);
				}
			}
			#endregion
			#region Texts
			if (modText != null) {
				writer.Write ((ushort) 0xDEAD);

				// Remove unused text entries
				bool[] used_text = new bool[modText.Length];
				byte last_used = 0;
				for (int y = 0; y < W_height; y++) {
					for (int x = 0; x < W_width; x++) {
						if (blocks[x, y].FG == 1000) {
							byte arg3 = blocks[x, y].arg3;
							if (arg3 >= modText.Length)
								throw new Exception ("Text array incomplete. Can not find #" + arg3);
							used_text[arg3] = true;
							last_used = Math.Max (arg3, last_used);
						}
					}
				}

				writer.Write (last_used);
				for (int i = 0; i <= last_used; i++) {
					if (!used_text[i]) {
						modText[i] = null;
						writer.Write ((byte) 0);
						continue;
					}
					if (string.IsNullOrEmpty (modText[i]))
						modText[i] = "#ERROR";
					writer.Write ((byte) modText[i].Length);
					writer.Write (enc.GetBytes (modText[i]));
				}
			}
			#endregion

			writer.Close ();
			stream.Close ();

			return stream.ToArray ();
		}

		void deserializeEntry (BinaryReader reader, ref SaveEntry dat) {
			int header = reader.ReadUInt16 ();
			if (header == 0xDEAD) {
				// Continue until world is done
				dat.y = W_height;
				return;
			}

			bool readHeader = true;
			for (int i = 0; i < DB_FLAGS.Length; i++) {
				if ((header & DB_FLAGS[i]) == 0)
					continue;

				int value = 0;
				if (readHeader) {
					value = header & ~DB_FLAGS_MASK;
					readHeader = false;
				} else if (i == 3 || i == 4)
					value = reader.ReadByte ();
				else
					value = reader.ReadUInt16 ();


				switch (i) {
					case 0: dat.x = Math.Min (value, W_width - 1); break;
					case 1: dat.y = Math.Min (value, W_height - 1); break;
					case 2: dat.FG = value; break;
					case 3: dat.BG = value; break;
					case 4: dat.arg3 = (byte) value; break;
				}
			}
		}

		void deserializeData (byte[] data) {
			#region Define variables
			MemoryStream stream = new MemoryStream (data);
			BinaryReader reader = new BinaryReader (stream);

			uint signature = reader.ReadUInt32 ();
			if (signature < 0xC0FFEE02 || signature > 0xC0FFEE03)
				throw new Exception ("Invalid data signature, got: " + signature.ToString ("X4"));

			int t_width = W_width;

			if (signature >= 0xC0FFEE03) {
				t_width = reader.ReadUInt16 ();
			}

			SaveEntry cur = new SaveEntry (),
				next = new SaveEntry ();

			for (int i = 0; i < DB_FLAGS.Length; i++)
				DB_FLAGS_MASK |= DB_FLAGS[i];
			#endregion

			bool first = true;
			while (cur.y < W_height) {
				deserializeEntry (reader, ref next);

				if (next.y == cur.y && next.x == cur.x && !first)
					next.x = Math.Min (cur.x + 1, t_width - 1);
				if (next.y == cur.y && next.x - cur.x < 0)
					next.y = cur.y + 1;

				first = false;
				while (cur.y < W_height) {
					// Stop when tail is reached
					if (cur.x == next.x && cur.y == next.y)
						break;

					int args = getBlockArgCount (cur.FG);
					if (cur.BG != 0 && cur.BG < 500)
						cur.BG += 480;

					Bindex b = new Bindex ();
					b.FG = cur.FG;
					b.BG = cur.BG;

					if (args > 0) {
						b.arg3 = cur.arg3;
						if (args == 1) {
							// Everything but portals
							int special_id = BlockToSPId (cur.FG);
							if (Nblock[special_id, cur.arg3] == null)
								Nblock[special_id, cur.arg3] = new Block ();

							Nblock[special_id, cur.arg3].Set (cur.x, cur.y, true);
						}
					} else if (b.FG != 0) {
						if (Nblock[0, b.FG] == null)
							Nblock[0, b.FG] = new Block ();

						Nblock[0, b.FG].Set (cur.x, cur.y, true);
					}

					if (cur.BG >= 500) {
						if (Nblock[1, b.BG - 500] == null)
							Nblock[1, b.BG - 500] = new Block ();

						Nblock[1, b.BG - 500].Set (cur.x, cur.y, true);
					}

					blocks[cur.x, cur.y] = b;

					// Jump to next line
					cur.x++;
					if (cur.x >= t_width) {
						cur.x = 0;
						cur.y++;
					}
				}
				// Push tail to new head
				cur = new SaveEntry (next);
			}
			#region Portals
			for (int y = 0; y < W_height; y++) {
				for (int x = 0; x < t_width; x++) {
					if (blocks[x, y].FG != 242)
						continue;

					Bindex b = new Bindex (blocks[x, y]);
					b.arg4 = reader.ReadByte ();
					b.arg5 = reader.ReadByte ();

					if (PBlock[b.arg3, b.arg4, b.arg5] == null)
						PBlock[b.arg3, b.arg4, b.arg5] = new Block ();
					PBlock[b.arg3, b.arg4, b.arg5].Set (x, y, true);
					blocks[x, y] = b;
				}
			}
			#endregion
			#region Texts
			if (stream.Position + 2 < stream.Length) {
				ushort security = reader.ReadUInt16 ();

				if (security == 0xDEAD) {
					modText = new string[reader.ReadByte () + 1];
					for (int i = 0; i < modText.Length; i++) {
						int length = reader.ReadByte ();
						if (length == 0)
							continue;
						modText[i] = enc.GetString (reader.ReadBytes (length));
					}
				}
			}
			#endregion

			reader.Close ();
			stream.Close ();
		}

		void saveWorldData (ref DatabaseObject o) {
			DatabaseArray ar = new DatabaseArray ();
			int index = 0;

			#region Fore-/background and special blocks
			for (int l = 0; l < (int) C.BLOCK_TYPES; l++) {
				for (int b = 0; b < (int) C.BLOCK_MAX; b++) {
					if (l == 0 && b == 0)
						continue;

					if (l > 0 && b > 0xFF)
						break;

					if (Nblock[l, b] == null) continue;
					if ((l == 0 && getBlockArgCount (b) > 0) || Nblock[l, b].used < 1)
						continue;

					int length = Nblock[l, b].pos.Length;
					byte[] bufferX = new byte[length * 2],
						bufferY = new byte[length * 2];

					int count = 0, real_block = b;
					if (l > 1)
						real_block = SPIdToBlock (l);
					else if (l == 1)
						real_block += 500;

					for (int i = 0; i < length; i++) {
						COORC pos = Nblock[l, b].pos[i];
						if (!isValidCoor (pos)) continue;

						bufferX[count] = (byte) (pos.x >> 8);
						bufferX[count + 1] = (byte) (pos.x % 256);
						bufferY[count] = (byte) (pos.y >> 8);
						bufferY[count + 1] = (byte) (pos.y % 256);
						count += 2;
					}

					if (count == 0) continue;
					if (bufferX.Length != count) {
						Array.Resize (ref bufferX, count);
						Array.Resize (ref bufferY, count);
					}

					DatabaseObject ob = new DatabaseObject ();
					ob.Set ("layer", l);
					ob.Set ("type", real_block);
					ob.Set ("x", bufferX);
					ob.Set ("y", bufferY);
					if (l > 1) {
						if (real_block == 1000) {
							// Get id of text
							int text_pos = 0;
							for (int i = 0; i < b; i++) {
								if (modText[i] != null)
									text_pos++;
							}
							ob.Set ("t", text_pos);
						} else {
							ob.Set ("a", b);
						}
					}
					ar.Add (index);
					ar.Set (index, ob);
					index++;
				}
			}
			#endregion

			#region Portals
			for (int r = 0; r < PBlock.GetLength (0); r++)
				for (int g = 0; g < 100; g++)
					for (int p = 0; p < 100; p++) {
						if (PBlock[r, g, p] == null) continue;
						if (PBlock[r, g, p].used < 1) continue;

						int length = PBlock[r, g, p].pos.Length;
						byte[] bufferX = new byte[length * 2],
							bufferY = new byte[length * 2];

						int count = 0;
						for (int i = 0; i < length; i++) {
							COORC pos = PBlock[r, g, p].pos[i];
							if (!isValidCoor (pos)) continue;

							bufferX[count] = (byte) (pos.x >> 8);
							bufferX[count + 1] = (byte) (pos.x % 256);
							bufferY[count] = (byte) (pos.y >> 8);
							bufferY[count + 1] = (byte) (pos.y % 256);
							count += 2;
						}

						if (count == 0) continue;
						if (bufferX.Length != count) {
							Array.Resize (ref bufferX, count);
							Array.Resize (ref bufferY, count);
						}

						DatabaseObject ob = new DatabaseObject ();
						ob.Set ("layer", 0);
						ob.Set ("type", 242);
						ob.Set ("x", bufferX);
						ob.Set ("y", bufferY);
						ob.Set ("pr", r);
						ob.Set ("pi", g);
						ob.Set ("pt", p);
						ar.Add (index);
						ar.Set (index, ob);
						index++;
					}
			#endregion

			o.Set ("worlddata", ar);
		}

		void readWorldData (ref DatabaseObject o, bool broadcast) {
			Message M_init = Message.Create ("reset");
			DatabaseArray ar = o.GetArray ("worlddata");

			for (int i = 0; i < ar.Count; i++) {
				//l,b,x,y,t/a
				if (!ar.Contains (i))
					continue;

				DatabaseObject ob = ar.GetObject (i);
				#region Header
				int l = ob.GetInt ("layer");
				int b = ob.GetInt ("type");
				int arg3 = -2, pId = -2, pTg = -2;
				bool isPortal = false;
				if (ob.Contains ("t")) {
					arg3 = ob.GetInt ("t");
				} else if (ob.Contains ("a")) {
					arg3 = ob.GetInt ("a");
				} else if (ob.Contains ("pr")) {
					arg3 = ob.GetInt ("pr");
					pId = ob.GetInt ("pi");
					pTg = ob.GetInt ("pt");
					isPortal = true;
				}
				#endregion

				byte[] pX = ob.GetBytes ("x"),
					pY = ob.GetBytes ("y");
				int[] x = new int[pX.Length / 2],
					y = new int[pY.Length / 2];
				for (int n = 0; n < x.Length; n++) {
					x[n] = pX[n * 2] << 8 | pX[(n * 2) + 1];
					y[n] = pY[n * 2] << 8 | pY[(n * 2) + 1];
					if (!isValidCoor (x[n], y[n]))
						continue;

					if (l == 1) {
						blocks[x[n], y[n]].BG = b;
						continue;
					}
					blocks[x[n], y[n]].FG = b;
					if (arg3 >= 0) {
						blocks[x[n], y[n]].arg3 = (byte) arg3;
						if (isPortal) {
							blocks[x[n], y[n]].arg4 = (byte) pId;
							blocks[x[n], y[n]].arg5 = (byte) pTg;
						}
					}
				}
				if (l == 1 || getBlockArgCount (b) == 0) {
					if (broadcast) M_init.Add (b, l, pX, pY);
					Nblock[l, (l == 0 ? b : b - 500)] = new Block (x, y);
					continue;
				}

				if (arg3 < 0)
					continue;

				if (isPortal) {
					if (arg3 < PBlock.GetLength (0) && pId <= 100 && pTg <= 100) {
						if (broadcast) M_init.Add (b, 0, pX, pY, arg3, pId, pTg);
						PBlock[arg3, pId, pTg] = new Block (x, y);
					}
					continue;
				}
				Nblock[BlockToSPId (b), arg3] = new Block (x, y);

				if (!broadcast)
					continue;

				if (b != 1000) {
					M_init.Add (b, 0, pX, pY, arg3);
				} else if (modText != null) {
					if (modText[arg3] != null) {
						M_init.Add (b, 0, pX, pY, modText[arg3]);
					}
				}
			}

			if (broadcast)
				Broadcast (M_init);
		}

#if INDEV
		void save_worlddata(Player pl, bool kick_all = false) {
			pl.Send("write", "* ERROR", "You can not save a world in the indev mode.";
			pl.Send("saved");
		}
#else
		void save_worlddata (Player pl, bool kick_all = false) {
			if (!W_isSaved)
				return;

			PlayerIO.BigDB.LoadOrCreate ("Worlds", RoomId, delegate (DatabaseObject o) {
				Cleanup_Timer ();
				if (kick_all)
					W_isSaved = false;

				#region Texts
				DatabaseArray txt = new DatabaseArray ();
				for (int i = 0; i < modText.Length; i++) {
					if (!string.IsNullOrEmpty (modText[i]))
						txt.Add (modText[i]);
				}

				if (o.Contains ("text"))
					o.Remove ("text");
				if (txt.Count > 0 && !W_experimental_saving)
					o.Set ("text", txt);
				#endregion

				if (W_experimental_saving) {
					if (o.Contains ("worlddata"))
						o.Remove ("worlddata");

					o.Set ("worlddata2", serializeData ());
				} else {
					if (o.Contains ("worlddata2"))
						o.Remove ("worlddata2");

					saveWorldData (ref o); // Regular way
				}
				o.Save ();

				pl.Send ("saved");
				W_isLoading = false;
				if (kick_all) {
					W_resized = true;
					foreach (Player p in Players) {
						p.isAdmin = false;
						p.canEdit = false;
						p.say_counter = 99;
						p.mWarns = 99;
						p.Send ("info", "World changed", "The world dimensions changed. Please rejoin.");
						p.Disconnect ();
					}
				}
			});
		}
#endif
		void load_worlddata (bool respawn = false, bool init = false) {
			W_isLoading = true;
			PlayerIO.BigDB.Load ("Worlds", RoomId, delegate (DatabaseObject o) {
				#region Verify database object
				bool canLoad = false;
				if (o != null) {
					if (o.ExistsInDatabase) {
						if (o.Contains ("owner"))
							canLoad = true;
					}
				}
				if (!canLoad) {
					clear_world (false);
					return;
				}
				#endregion

				#region Load on-init values
				if (init) {
					if (o.Contains ("name"))
						W_title = o.GetString ("name");
					if (o.Contains ("plays"))
						W_plays = o.GetInt ("plays");
					if (o.Contains ("admins"))
						admins = new pList<string> (o.GetString ("admins").Split (','));

					W_width = o.GetInt ("width");
					W_height = o.GetInt ("height");

					RoomData["name"] = W_title;
					RoomData["plays"] = W_plays + "";
					RoomData["needskey"] = "yup";

					PlayerIO.BigDB.Load ("PlayerObjects", o.GetString ("owner"), delegate (DatabaseObject b) {
						if (b != null) {
							W_Owner = b.GetString ("name");
							W_isSaved = true;
						}
						RoomData["owner"] = W_Owner;
						RoomData["owned"] = "true";
						RoomData.Save ();
					});
				}
				#endregion

				clear_world (false, false);

				#region Get texts
				DatabaseArray texts = new DatabaseArray ();
				if (o.Contains ("text"))
					texts = o.GetArray ("text");

				modText = new string[texts.Count + 10];
				for (int i = 0; i < texts.Count; i++) {
					modText[i] = texts.GetString (i);
				}
				#endregion

				if (o.Contains ("worlddata")) {
					readWorldData (ref o, !init);

					if (respawn)
						respawn_players (true);
				} else if (o.Contains ("worlddata2")) {
					deserializeData (o.GetBytes ("worlddata2"));

					if (!init)
						W_broadcast_level = respawn ? 2 : 1;
					else
						W_experimental_saving = true;
				}
				W_isLoading = false;
				W_gotEdited = false;
			});
		}
		#endregion
		void getWorldDataMessage (ref Message m) {
			#region Fore-/background and special blocks
			for (int l = 0; l < (int) C.BLOCK_TYPES; l++)
				for (int b = 0; b < (int) C.BLOCK_MAX; b++) {
					if (l == 0 && b == 0)
						continue;

					if (l > 0 && b > 0xFF)
						break;

					if (Nblock[l, b] == null) continue;
					if ((l == 0 && getBlockArgCount (b) > 0) || Nblock[l, b].used < 1)
						continue;

					int length = Nblock[l, b].pos.Length;
					byte[] bufferX = new byte[length * 2],
						bufferY = new byte[length * 2];

					int count = 0, real_block = b;
					if (l > 1)
						real_block = SPIdToBlock (l);
					else if (l == 1)
						real_block += 500;

					for (int i = 0; i < length; i++) {
						COORC pos = Nblock[l, b].pos[i];
						if (!isValidCoor (pos)) continue;

						bufferX[count] = (byte) (pos.x >> 8);
						bufferX[count + 1] = (byte) (pos.x % 256);
						bufferY[count] = (byte) (pos.y >> 8);
						bufferY[count + 1] = (byte) (pos.y % 256);
						count += 2;
					}

					if (count == 0) continue;
					if (bufferX.Length != count) {
						Array.Resize (ref bufferX, count);
						Array.Resize (ref bufferY, count);
					}

					if (l < 2) {
						m.Add (real_block, l, bufferX, bufferY);
						continue;
					}
					if (real_block == 1000) {
						m.Add (real_block, 0, bufferX, bufferY, (modText[b] != null ? modText[b] : "INTERNAL ERROR"));
					} else m.Add (real_block, 0, bufferX, bufferY, b);
				}
			#endregion

			#region Portals
			for (int r = 0; r < PBlock.GetLength (0); r++)
				for (int g = 0; g < 100; g++)
					for (int p = 0; p < 100; p++) {
						if (PBlock[r, g, p] == null) continue;
						if (PBlock[r, g, p].used < 1) continue;

						int length = PBlock[r, g, p].pos.Length;
						byte[] bufferX = new byte[length * 2],
							bufferY = new byte[length * 2];

						int count = 0;
						for (int i = 0; i < length; i++) {
							COORC pos = PBlock[r, g, p].pos[i];
							if (!isValidCoor (pos)) continue;

							bufferX[count] = (byte) (pos.x >> 8);
							bufferX[count + 1] = (byte) (pos.x % 256);
							bufferY[count] = (byte) (pos.y >> 8);
							bufferY[count + 1] = (byte) (pos.y % 256);
							count += 2;
						}

						if (count == 0) continue;
						if (bufferX.Length != count) {
							Array.Resize (ref bufferX, count);
							Array.Resize (ref bufferY, count);
						}

						m.Add (242, 0, bufferX, bufferY, r, g, p);
					}
			#endregion
		}

		void Cleanup_Timer () {
			Broadcast ("updatemeta", W_Owner, W_title, W_plays);

			if (modText == null)
				return;

			#region Remove unused text from the array
			int b = BlockToSPId (1000);
			if (Nblock[b, 0] == null)
				return;

			bool[] is_used = new bool[modText.Length];
			for (int i = 0; i < modText.Length && i < (int) C.BLOCK_MAX; i++) {
				if (Nblock[b, i] == null)
					continue;

				if (Nblock[b, i].used < 1)
					continue;

				is_used[i] = true;
			}
			for (int i = 0; i < modText.Length; i++) {
				if (!is_used[i] && modText[i] != null)
					modText[i] = null;
			}
			#endregion
		}

		void initPlayers () {
			// Broadcast new DB-type world, spread the load
			if (W_broadcast_level > 0) {
				Message M_init = Message.Create ("reset");
				getWorldDataMessage (ref M_init);
				Broadcast (M_init);

				if (W_broadcast_level == 2)
					respawn_players (true);
				W_broadcast_level = 0;
				return;
			}

			if (W_isLoading || Nblock == null || PBlock == null)
				return;

			W_can_save = true;

			byte[] binary_data = null;
			bool spawns_parsed = false;
			foreach (Player pl in Players) {
				if (pl.say_counter > 6) {
					pl.Send ("info", "Getting you off", "You seem to share a lot of knowledge. Not everybody likes the flood of messages.");
					pl.Disconnect ();
					continue;
				}
				pl.say_counter = 0;
				pl.system_messages = 0;
				if (pl.moved > moveLimit) {
					foreach (Player p in Players) {
						if (p.Id == pl.Id) continue;
						p.Send ("m", pl.Id, pl.posX, pl.posY, pl.speedX, pl.speedY,
									pl.gravityX, pl.gravityY, pl.keyX, pl.keyY);
					}
				}
				pl.moved = 0;

				if (!pl.send_init)
					continue;

				if (W_Owner == pl.Name ||
					("x." + W_Owner) == pl.Name ||
					(admins.Contains (pl.Name) && W_isSaved)) {
					pl.isAdmin = true;
					pl.canEdit = true;
				}
				if (!spawns_parsed) {
					parseSpawns ();
					spawns_parsed = true;
				}
				COOR c = get_next_spawn ();
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;

				bool W_isTutorial = false;
				Message M_init = Message.Create ("init", W_Owner, W_title, W_plays.ToString (), derot13 (W_rot13),
					pl.Id, pl.posX, pl.posY, pl.Name, pl.canEdit, pl.isAdmin,
					W_width, W_height, W_isTutorial && !pl.isModerator && !pl.isAdmin);

				if (pl.init_binary) {
					if (binary_data == null)
						binary_data = serializeData ();

					M_init.Add (binary_data);
				} else {
					getWorldDataMessage (ref M_init);
				}

				pl.Send (M_init);

				if (!pl.isGuest) {
					for (int i = 0; i < oldChat0.Length; i++) {
						if (!string.IsNullOrEmpty (oldChat0[i]))
							pl.Send ("write", oldChat0[i], oldChat1[i]);
					}
				}

				#region Misc
				foreach (Player p in Players) {
					if (p.Id != pl.Id) {
						pl.Send ("add", p.Id, p.Name, p.Face, p.posX, p.posY, p.god_mode, p.mod_mode, !p.isGuest, p.coins);
						p.Send ("add", pl.Id, pl.Name, pl.Face, pl.posX, pl.posY, false, false, !pl.isGuest, 0);
					}
				}
				if (keys[0] >= 1)
					pl.Send ("hide", "red");

				if (keys[1] >= 1)
					pl.Send ("hide", "green");

				if (keys[2] >= 1)
					pl.Send ("hide", "blue");

				if (W_crown != -1)
					pl.Send ("k", W_crown);
				#endregion
				pl.send_init = false;
				pl.isInited = true;
			}
		}

		void killPlayers () {
			kill_active = true;
			Message msg = Message.Create ("tele", false);
			int count = 0;
			parseSpawns ();
			foreach (Player pl in Players) {
				if (pl.gotCoin) {
					pl.gotCoin = false;
					Broadcast ("c", pl.Id, pl.coins);
				}

				if (!pl.isDead) continue;
				pl.isDead = false;
				if (pl.god_mode || pl.mod_mode) continue;
				#region dead
				COOR c = new COOR ();

				if (getBlock (0, pl.cPointX, pl.cPointY) == 104) {
					c.x = pl.cPointX;
					c.y = pl.cPointY;
				} else {
					c = get_next_spawn ();
				}

				pl.speedX = 0;
				pl.speedY = 0;
				pl.gravityX = 0;
				pl.gravityY = 0;
				pl.keyX = 0;
				pl.keyY = 0;
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;

				msg.Add (pl.Id, pl.posX, pl.posY);
				#endregion
				count++;
			}
			if (count > 0) Broadcast (msg);
			kill_active = false;
		}

		int getBlock (int l, int x, int y) {
			if (!isValidCoor (x, y))
				return -1;

			if (l == 0)
				return blocks[x, y].FG;
			else if (l == 1)
				return blocks[x, y].BG;
			return -1;
		}

		int countBlock (int b) {
			int count = 0;
			for (int y = 0; y < W_height; y++)
				for (int x = 0; x < W_width; x++)
					if (blocks[x, y].FG == b)
						count++;
			return count;
		}

		byte getBlockArgCount (int id) {
			if (id == 43 || id == 77 || id == 1000) // 83
				return 1;
			if (id == 242)
				return 3;
			return 0;
		}

		void parseSpawns () {
			if (Nblock == null || Nblock[0, 255] == null) {
				spawnCoor = new COOR[0];
				return;
			}
			spawnCoor = new COOR[Nblock[0, 255].used];

			int count = 0;
			for (int i = 0; i < Nblock[0, 255].pos.Length; i++) {
				COORC pos = Nblock[0, 255].pos[i];
				if (isValidCoor (pos)) {
					spawnCoor[count].x = pos.x;
					spawnCoor[count].y = pos.y;
					count++;
				}
			}
			if (count < spawnCoor.Length) {
				Array.Resize (ref spawnCoor, count);
			}
		}

		COOR get_next_spawn () {
			COOR c = new COOR ();
			c.x = 1;
			c.y = 1;
			if (spawnCoor.Length == 0) return c;

			for (int i = 0; i < 40; i++) {
				cSpawn++;
				if (cSpawn >= spawnCoor.Length) {
					cSpawn = 0;
				}

				if (getBlock (0, spawnCoor[cSpawn].x, spawnCoor[cSpawn].y) == 255) {
					return spawnCoor[cSpawn];
				}
			}
			return c;
		}

		void clear_world (bool createBorder = false, bool loadingDone = true) {
			W_isLoading = true;

			Nblock = new Block[(int) C.BLOCK_TYPES, (int) C.BLOCK_MAX];
			PBlock = new Block[5, 101, 101];
			blocks = new Bindex[W_width, W_height];
			modText = new string[200];

			if (createBorder) {
				int size = (2 * W_width) + (2 * W_height) - 4;
				int[] xpos = new int[size],
					ypos = new int[size];
				int c = 0;
				for (int i = 0; i < W_width; i++) {
					xpos[c] = i;
					xpos[c + 1] = i;
					ypos[c] = 0;
					ypos[c + 1] = W_height - 1;
					c += 2;
					blocks[i, 0].FG = 9;
					blocks[i, W_height - 1].FG = 9;
				}
				for (int i = 1; i < W_height - 1; i++) {
					xpos[c] = 0;
					xpos[c + 1] = W_width - 1;
					ypos[c] = i;
					ypos[c + 1] = i;
					c += 2;
					blocks[0, i].FG = 9;
					blocks[W_width - 1, i].FG = 9;
				}

				Nblock[0, 9] = new Block (xpos, ypos);
			}
			W_gotEdited = false;

			if (loadingDone) W_isLoading = false;
		}

		void broadcast_clear_world () {
			clear_world (true);
			foreach (Player p in Players) {
				if (p.coins > 0) p.gotCoin = true;
				p.coins = 0;
			}
			Broadcast ("clear", W_width, W_height);
		}

		int SPIdToBlock (int id) {
			if (id == 2)
				return 43;
			if (id == 3)
				return 77;
			if (id == 4)
				return 1000;
			return 9;
			//if (id == 2) id = 83;
			//if (id == 3) id = 242;
		}

		int BlockToSPId (int id) {
			if (id == 43)
				return 2;
			if (id == 77)
				return 3;
			if (id == 1000)
				return 4;
			return 0;
			//if (id == 83) id = 2;
			//if (id == 242) id = 3;
		}

		void addLog (string p, string s) {
			for (int i = logbook.Length - 1; i > 0; i--) {
				logbook[i] = logbook[i - 1];
			}
			logbook[0] = p.ToUpper () + ' ' + s;
		}

		void removeOldBlock (int x, int y, int id, int arg3) {
			if (id == 242) {
				int oldId = blocks[x, y].arg4;
				int oldTg = blocks[x, y].arg5;
				if (PBlock[arg3, oldId, oldTg] != null)
					PBlock[arg3, oldId, oldTg].Remove (x, y);
				return;
			}
			if (getBlockArgCount (id) > 0) {
				int specialId = BlockToSPId (id);
				if (Nblock[specialId, arg3] != null)
					Nblock[specialId, arg3].Remove (x, y);
			} else if (Nblock[0, id] != null) {
				Nblock[0, id].Remove (x, y);
			}
		}

		bool hasAccess (Player p, Rights level, bool syntax = true) {
			bool allowed = false;
			string priv = "";
			if (level == Rights.Edit) {
				allowed = p.canEdit;
				priv = "Edit access";
			} else {
				allowed = get_rights (p) >= level;
				priv = level.ToString ();
			}
			if (p.system_messages < sys_msg_max) {
				if (!allowed) {
					string txt = "You have insufficient powers for this command.";
					if (priv != "") {
						txt += "\nRequired power level: " + priv;
					}
					p.Send ("write", SYS, txt);
				} else if (!syntax) {
					p.Send ("write", SYS, "Syntax error. See /help [command] for further information.");
				}
			}
			p.system_messages++;
			return allowed && syntax;
		}

		Rights get_rights (Player p) {
			if (p.isModerator) return Rights.Moderator;
			if (p.Name == W_Owner || p.Name == "x." + W_Owner) return Rights.Owner;
			if (p.isAdmin) return Rights.Admin;
			if (p.isVigilant) return Rights.Vigilant;
			if (!p.isGuest) return Rights.Normal;
			return Rights.None;
		}

		void handle_spam (Player pl, string msg) {
			short percent = isEqualTo (msg, pl.last_said);
			pl.say_counter++;
			pl.last_said = msg;

			if (pl.isBot)
				percent -= 20;

			if (percent < 50) {
				pl.sameText--;
				return;
			}

			while (percent >= 50) {
				pl.sameText++;
				percent -= 20;
			}
		}

		bool isValidCoor (COORC pos) {
			if (pos == null)
				return false;
			return isValidCoor (pos.x, pos.y);
		}

		bool isValidCoor (int x, int y) {
			return (x >= 0 && y >= 0 && x < W_width && y < W_height);
		}

		string generate_rot13 () {
			char[] buffer = new char[3];
			string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvw";

			for (int i = 0; i < 3; i++) {
				buffer[i] = chars[info.random.Next (chars.Length)];
			}

			return "." + new string (buffer);
		}

		string derot13 (string arg1) {
			int num = 0;
			string str = "";
			for (int i = 0; i < arg1.Length; i++) {
				num = arg1[i];
				if ((num >= 0x61) && (num <= 0x7a)) {
					if (num > 0x6d) num -= 13; else num += 13;
				} else if ((num >= 0x41) && (num <= 90)) {
					if (num > 0x4d) num -= 13; else num += 13;
				}
				str += ((char) num);
			}
			return str;
		}
		short isEqualTo (string text_1, string text_2) {
			if (text_1.Length < 2 || text_2.Length < 2)
				return 60;

			char[] raw_1 = text_1.ToLower ().ToCharArray (),
					raw_2 = text_2.ToLower ().ToCharArray ();

			#region normalize
			for (int i = 0; i < raw_1.Length; i++) {
				char cur = raw_1[i];
				bool found = false;
				for (int k = 0; k < say_normal.Length; k++) {
					if (cur == say_normal[k]) {
						found = true;
						break;
					}
				}
				if (!found) raw_1[i] = '.';
			}
			for (int i = 0; i < raw_2.Length; i++) {
				char cur = raw_2[i];
				bool found = false;
				for (int k = 0; k < say_normal.Length; k++) {
					if (cur == say_normal[k]) {
						found = true;
						break;
					}
				}
				if (!found) raw_2[i] = '.';
			}
			#endregion

			int equals = 0,
				total = 0;
			char last_raw_1 = '-',
				last_raw_2 = '-';
			for (int i = 0; i < raw_1.Length; i++) {
				if (raw_1[i] == last_raw_1) continue;
				last_raw_1 = raw_1[i];
				total++;

				bool found = false;
				for (int k = 0; k < raw_2.Length && i < raw_1.Length; k++) {
					if ((raw_2[k] == last_raw_2) ||
						(found && raw_1[i] == last_raw_1)) continue;

					last_raw_2 = raw_2[k];
					if (raw_1[i] == raw_2[k]) {
						if (found || (k == 0 && i == 0)) {
							equals++;
							total++;
						}
						last_raw_1 = raw_1[i];
						i++;
						found = true;
					} else found = false;
				}
			}

			if (total > 0) {
				return (short) ((equals / (float) total) * 100 + 0.5);
			} else return 100;
		}

		bool is_yes (string r) {
			bool isyes = false;
			switch (r.ToLower ()) {
				case "yes":
				case "true":
				case "1":
				case "on":
				case "enable":
				case "allow":
					isyes = true;
					break;
			}
			return isyes;
		}

		void set_setting (Player p, bool newValue, ref bool setting, string description) {
			if (newValue != setting) {
				setting ^= true;
				string text = description + ": " + (setting ? "ON" : "OFF");

				addLog (p.Name, text);
				Broadcast ("write", SYS, text + " (" + p.Name.ToUpper () + ")");
				return;
			}

			p.Send ("write", SYS, "Nothing to change for setting `" + description + "´. It already was set to: " + (setting ? "ON" : "OFF"));
		}

		long getMTime () {
			TimeSpan t = (DateTime.Now - new DateTime (2014, 1, 1));
			return (long) t.TotalSeconds;
		}

		bool Contains (int[] arr, int n) {
			for (byte i = 0; i < arr.Length; i++) {
				if (arr[i] == n) return true;
			}
			return false;
		}
	}
}
