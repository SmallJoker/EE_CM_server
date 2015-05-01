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
using System.Text;
using PlayerIO.GameLibrary;

namespace EE_CM {
	// Constants
	enum C {
		BLOCK_FG_MAX = 500,
		BLOCK_BG_MAX = 200,
		WORLD_TYPES = 5,
		WORLDS_PER_PLAYER = 4,
		SMILIES = 54
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
	[RoomType("Indev10")]
#else
	[RoomType("Game10")]
#endif
	public class EENGameCode : Game<Player> {
		#region define
		Bindex[,] blocks;
		Block[][] Nblock;
		Block[,] FSblock;
		Block[, ,] PBlock;
		COOR[] spawnCoor;
		PlayerHistory[] usernames = new PlayerHistory[16];
		WorldInfo info = new WorldInfo();

		int[] gravity0Id = new int[] { 4, 112, 114, 115, 116, 117, 118 },
			specialBlocks = new int[] { 43, 77, /*83,*/ 242, 1000 };

		pList<string> banned = new pList<string>(),
			admins = new pList<string>();

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
			W_can_save = false;

		int W_width, W_height, W_plays,
			W_crown = -1,
			W_chatLimit = 150,
			W_Bcount = 0,
			moveLimit = 20,
			cSpawn = 0,
			W_type = -1,
			sys_msg_max = 3;

		string W_key = "",
			W_rot13,
			W_Owner,
			W_title,
			SYS = "* SYSTEM",
			say_normal = "abcdefghijklmnopqurstuvwxyz ";

		byte[] keys = new byte[3];
		#endregion

		public override void GameStarted() {
			PreloadPlayerObjects = true;

			W_Owner = "";
			W_title = "unknown";
			W_width = 200;
			W_height = 200;
			W_plays = 0;
			W_rot13 = generate_rot13();

			string prefix = RoomId[0] + "" + RoomId[1];
			RoomData["plays"] = "0";
			if (prefix == "PW" || prefix == "BW") {
				W_key = generate_rot13() + generate_rot13();
				load_worlddata(false, true);
			} else {
				clear_world(true);
				if (RoomData.ContainsKey("name"))
					W_title = RoomData["name"];
				else
					RoomData["name"] = W_title = "Untitled World";
				
				if (RoomData.ContainsKey("editkey")) {
					if (!string.IsNullOrEmpty(RoomData["editkey"])) {
						RoomData["needskey"] = "yup";
						W_key = RoomData["editkey"];
					} else {
						W_key = "";
						W_isOpen = true;
					}

					RoomData.Remove("editkey");
				}
			}
			if (W_key == "")
				W_isOpen = true;

			RoomData.Save();
			AddTimer(Keys_Timer, 200);
			AddTimer(Cleanup_Timer, 20000);
			AddTimer(initPlayers, 1500);
			AddTimer(killPlayers, 800);
		}

		public override void GameClosed() {
			if (!W_isSaved)
				return;

			PlayerIO.BigDB.Load("Worlds", RoomId, delegate(DatabaseObject o) {
				o.Set("name", W_title);
				o.Set("plays", W_plays);

				string admins_text = "";
				string[] admins_array = admins.GetData();

				for (int i = 0; i < admins_array.Length; i++) {
					if (admins_array[i] == null) continue;
					admins_text += admins_array[i] + ',';
				}

				if (admins_text.Length > 0)
					o.Set("admins", admins_text);
				else if (o.Contains("admins"))
					o.Remove("admins");

				o.Save();
			});
		}

		public override void UserJoined(Player pl) {
			string reason = HandleUserJoin(pl);

			if (reason == null)
				return;

			pl.say_counter = 99;
			pl.mWarns = 99;
			pl.Send("info", "Connecting failed", reason);
			pl.Disconnect();
		}

		string HandleUserJoin(Player pl) {
			long time = getMTime(),
				last_online = 0;

			for (int i = 0; i < usernames.Length; i++) {
				if (usernames[i] == null) continue;
				if (usernames[i].Id == pl.ConnectUserId) {
					last_online = Math.Max(usernames[i].join_time, last_online);
				}
			}
			if (time - last_online < 10)
				return "You create traffic and I am a traffic light.";

			if (W_resized)
				return "This world got resized. Please wait until it has fully closed.\nThanks.";

			if (!pl.PlayerObject.Contains("name"))
				return "You need to set an username first.";

			if (pl.PlayerObject.Contains("banned")) {
				if (!(pl.PlayerObject["banned"] is bool)) {
					long time_left = pl.PlayerObject.GetLong("banned") - getMTime();
					if (time_left > 20) {
						return ("This account has been banned from EE CM." +
							"Please wait " +
							Math.Round(time_left / (60.0 * 60), 2) +
							" hours until your ban expires.");
					}
				} else if (pl.PlayerObject.GetBool("banned"))
					return "This account has been banned from EE CM.";
			}

			string name = pl.PlayerObject.GetString("name");
			if (banned.Contains(name))
				return "You have been banned form this world.";

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
				if (name.Length < 3)
					return "You are using an invalid nickname.";
			} else name = "guest-" + (pl.Id + 1);

			pl.Name = name;
			pl.isGuest = isGuest;

			if (pl.PlayerObject.Contains("isModerator"))
				pl.isModerator = pl.PlayerObject.GetBool("isModerator");
			
			if (pl.PlayerObject.Contains("isVigilant"))
				pl.isVigilant = pl.PlayerObject.GetBool("isVigilant");
			
			// Use isGuest for non-chatters
			if (!isGuest && pl.PlayerObject.Contains("chatbanned"))
				pl.isGuest = pl.PlayerObject.GetBool("chatbanned");
			
			if (pl.PlayerObject.Contains("face"))
				pl.Face = pl.PlayerObject.GetInt("face");
			
			int found = 0;
			System.Net.IPAddress ip = pl.IPAddress;
			foreach (Player p in Players) {
				if (p.Name == name || p.Name == "x." + name) {
					found += 3;
				} else if (ip.Equals(p.IPAddress)) {
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
					RoomData["plays"] = W_plays.ToString();
					RoomData.Save();
				}
			}

			if (usernames.Length < pl.Id + 1)
				Array.Resize(ref usernames, pl.Id + 10);
			
			usernames[pl.Id] = new PlayerHistory();
			usernames[pl.Id].Name = pl.Name;
			usernames[pl.Id].Id = pl.ConnectUserId;
			usernames[pl.Id].join_time = getMTime();
			return null;
		}

		public override void UserLeft(Player pl) {
			if (W_crown == pl.Id) W_crown = -1;
			if (!pl.isInited)
				return;

			pl.isInited = false;
			Broadcast("left", pl.Id);
#if !INDEV
			pl.GetPlayerObject(delegate(DatabaseObject obj) {
				obj.Set("face", pl.Face);
				obj.Save();
			});
#endif
		}

		// Split the messages and seperate the code
		public override void GotMessage(Player pl, Message m) {
			if (m.Type == "init" ||
				m.Type == "botinit" ||
				m.Type == "access") {

				MainGameFunc(pl, m);
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
					HandleBlock(pl, m);
				}
				return;
			}

			if (m.Type == "say" ||
				m.Type == "m") {

				if (pl.isInited && !W_resized) {
					PlayerInteract(pl, m);
				}
				return;
			}

			if (m.Type == "key" ||
				m.Type == "name" ||
				m.Type == "clear" ||
				m.Type == "save") {

				if (pl.isOwner && !W_isLoading) {
					OwnerInteract(pl, m);
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
					GamePlayFunc(pl, m);
				}
				return;
			}
		}

		void MainGameFunc(Player pl, Message m) {
			#region Init
			if ((m.Type == "init" || m.Type == "botinit") && !pl.isInited) {
				pl.isInited = true;
				if (W_key == "")
					pl.canEdit = !pl.isModerator;
				
				if (m.Type == "botinit") {
					pl.isBot = true;
					pl.Name = "x." + pl.Name;
				} else
					pl.firstFace = true;
				
				if (W_upgrade) {
					// Send dummy output for updated worlds
					pl.Send("init", "updateOwner", "updateRoom", "9999", "ofrt", pl.Id, 16, 16, "", false, false, 2, 2, false);
					pl.Send("upgrade");
				} else
					pl.initWait = true;
				return;
			}
			#endregion
			if (m.Type == "access") {
				if (!(m[0] is string) || !pl.isInited) {
					pl.Disconnect();
					return;
				}
				if (m.GetString(0) == W_key || pl.isModerator) {
					pl.code_tries = 0;
					pl.canEdit = true;
					pl.Send("access");
				} else {
					pl.code_tries++;
					if (pl.code_tries > 50)
						pl.Disconnect();
				}
				return;
			}
		}

		void HandleBlock(Player pl, Message m) {
			#region Block placement
			if (m.Type == W_rot13) {
				if (isEditBlocked || m.Count < 4 || !pl.canEdit)
					return;

				for (uint i = 0; i < 4; i++) {
					if (!(m[i] is int))
						return;
				}
				int l = m.GetInt(0),
					x = m.GetInt(1),
					y = m.GetInt(2),
					b = m.GetInt(3);

				if (!isValidCoor(x, y) || (l != 0 && l != 1) || b < 0)
					return;

				int id = getBlock(l, x, y);
				if (id < 0)
					return;

				int org3 = blocks[x, y].arg3;

				#region get block info
				if (pl.getBlockInfo) {
					string text = "Id: " + id,
						blPlacer = "?";
					if (l == 0) {
						if (id == 242) {
							text += "\nRotation: " + org3;
							text += "\nPortal-Id: " + blocks[x, y].pId;
							text += "\nTarget portal: " + blocks[x, y].pTarget;
						} else if (id == 43 || id == 77) {
							text += "\nArg3: " + org3;
						} else if (id == 1000) {
							string ktx = "[ERROR]";
							if (org3 >= 0 && org3 < modText.Length) {
								if (modText[org3] != null)
									ktx = modText[org3];
							}
							text += "\nText: " + ktx;
						}
						if (blocks[x, y].FGp > 0) {
							if (usernames[blocks[x, y].FGp] != null)
								blPlacer = usernames[blocks[x, y].FGp].Name;
						}
					} else {
						if (blocks[x, y].BGp > 0) {
							if (usernames[blocks[x, y].BGp] != null)
								blPlacer = usernames[blocks[x, y].BGp].Name;
						}
					}
					text += "\nPlacer: " + blPlacer.ToUpper();
					pl.Send("write", SYS, (l == 0 ? "Block" : "Background") + " [" + x.ToString() + '|' + y.ToString() + "]: \n" + text);
					pl.getBlockInfo = false;
					return;
				}
				#endregion

				Message block_msg = null;
				if (!Contains(specialBlocks, b)) {
					if (b == id || m.Count != 4) return;
#if INDEV
					if (l == 0 && b < (int)C.BLOCK_FG_MAX) {
					#region foreground
						removeOldBlock(x, y, id, org3);
						if (b != 0) {
							if (Nblock[0][b] == null)
								Nblock[0][b] = new Block();

							Nblock[0][b].Set(x, y);
						}

						blocks[x, y].FG = b;
						blocks[x, y].FGp = pl.Id;
						blocks[x, y].arg3 = 0;
						blocks[x, y].pId = 0;
						blocks[x, y].pTarget = 0;
					#endregion
					} else if (l == 1 && ((b >= 500 && b - 500 < (int)C.BLOCK_BG_MAX) || b == 0)) {
					#region background
						if (id != 0) {
							if (Nblock[1][id - 500] != null)
								Nblock[1][id - 500].Remove(x, y);
						}

						if (b != 0) {
							if (Nblock[1][b - 500] == null)
								Nblock[1][b - 500] = new Block();

							Nblock[1][b - 500].Set(x, y);
						}
						blocks[x, y].BG = b;
						blocks[x, y].BGp = pl.Id;
					#endregion
					} else return;
					block_msg = Message.Create("b", l, x, y, b);
#else
					#region normalBlock
					if (l == 0 && b < (int)C.BLOCK_FG_MAX) {
						#region foreground
						bool edit = false;
						if (b >= 0 && b <= 36) edit = true;		// Default
						if (b >= 37 && b <= 42) edit = true;	// Beta
						if (b == 44) edit = true;				// Black
						if (b >= 45 && b <= 49) edit = true;	// Factory
						if (b == 50 || b == 243) edit = true;	// Secrets
						if (b >= 51 && b <= 58) edit = true;	// Glass
						if (b == 59) edit = true;				// Summer 2011
						if (b >= 60 && b <= 67) edit = true;	// Candy
						if (b >= 68 && b <= 69) edit = true;	// Halloween 2011
						if (b >= 70 && b <= 76) edit = true;	// Minerals
						if (b >= 78 && b <= 82) edit = true;	// Christmas 2011
						if (b >= 84 && b <= 89) edit = true;	// Tiles
						if (b == 90) edit = true;				// White basic
						if (b == 91 || b == 92) edit = true;	// Swamp - One way
						if (b >= 93 && b <= 95) edit = true;	// Ice
						if (b >= 96 && b <= 98) edit = true;	// Gothic
						if (b >= 100 && b <= 101) edit = true;	// Coins
						//if (b >= 110 && b <= 111 && pl.isOwner) edit = true;
						if (b >= 400 && b <= 405) edit = true;	// Materials
						if (b >= 406 && b <= 411) edit = true;	// Wall


						// Decoration
						if (b == 102 && pl.isOwner) edit = true; // To be removed
						if (id == 102 && !(pl.isOwner || pl.isModerator)) edit = false; // To be removed
						if (b == 103 && (pl.isOwner || pl.isModerator)) {
							edit = true; // Codeblock
							if (Nblock[0][b] != null) {
								if (Nblock[0][b].used > 2)
									edit = false;
							}
						}
						if (b == 104) {
							edit = true; // Checkpoint
							if (Nblock[0][b] != null) {
								if (Nblock[0][b].used > 600)
									edit = false;
							}
						}
						if (b == 105) edit = true; // Hazard (Spikes)
						if ((b == 106 || b == 107) && pl.isOwner) {
							edit = true; // Trophy
							if (Nblock[0][b] != null) {
								if (Nblock[0][b].used >= 1)
									edit = false;
							}
						}
						if (b == 108 || b == 109) edit = true;	// Water
						if (b == 112) edit = true;				// Ladder
						if (b == 113) edit = true;				// Sand (slow)
						if (b == 118) edit = true;				// Swamp-water
						if (b >= 114 && b <= 117) edit = true;	// Boost

						//end special
						if (b == 121) edit = true;				// Invisible
						if (b == 223) edit = true;				// Halloween 2011 Trophy
						if (b == 227) edit = true;				// Candy
						if (b >= 218 && b <= 222) edit = true;	// Christmas 2011
						if (b >= 224 && b <= 226) edit = true;	// Halloween 2011
						if (b >= 228 && b <= 232) edit = true;	// Summer 2011
						if (b >= 233 && b <= 240) edit = true;	// Spring 2011 Grass
						if (b == 241 && pl.isOwner) {
							edit = true; // Diamond
							if (Nblock[0][b] != null) {
								if (Nblock[0][b].used > 10)
									edit = false;
							}
						}
						if (b >= 244 && b <= 248) edit = true; // New year 2010
						if (b >= 249 && b <= 254) edit = true; // Christmas 2010
						if (b == 255 && (pl.isOwner || pl.isModerator)) {
							edit = true; // Spawnpoint
							if (Nblock[0][b] != null) {
								if (Nblock[0][b].used > 60)
									edit = false;
							}
						}
						if (b >= 256 && b <= 264) edit = true; // Swamp plants
						if (b >= 265 && b <= 268) edit = true; // Snow and ice
						if (b >= 269 && b <= 273) edit = true; // Gothic

						if (!edit)
							return;

						removeOldBlock(x, y, id, org3);
						if (b != 0) {
							if (Nblock[0][b] == null)
								Nblock[0][b] = new Block();

							Nblock[0][b].Set(x, y);
						}

						blocks[x, y].FG = b;
						blocks[x, y].FGp = pl.Id;
						blocks[x, y].arg3 = 0;
						blocks[x, y].pId = 0;
						blocks[x, y].pTarget = 0;
						#endregion
					} else if (l == 1 && ((b >= 500 && b - 500 < (int)C.BLOCK_BG_MAX) || b == 0)) {
						#region background
						bool edit = false;
						if (b == 0) edit = true;
						if (b >= 500 && b <= 512) edit = true;	// Basic
						if (b >= 513 && b <= 519) edit = true;	// Checker
						if (b >= 520 && b <= 526) edit = true;	// Dark
						if (b >= 527 && b <= 532) edit = true;	// Pastel
						if (b >= 533 && b <= 538) edit = true;	// Canvas
						if (b >= 539 && b <= 540) edit = true;	// Candy
						if (b >= 541 && b <= 544) edit = true;	// Halloween 2011
						if (b >= 545 && b <= 549) edit = true;	// Wallpaper
						if (b >= 550 && b <= 555) edit = true;	// Tile
						if (b >= 556 && b <= 558) edit = true;	// Ice
						if (b == 559) edit = true;				// Gothic
						if (b >= 560 && b <= 564) edit = true;	// Fancy

						if (!edit) return;

						if (id != 0) {
							if (Nblock[1][id - 500] != null)
								Nblock[1][id - 500].Remove(x, y);
						}

						if (b != 0) {
							if (Nblock[1][b - 500] == null)
								Nblock[1][b - 500] = new Block();

							Nblock[1][b - 500].Set(x, y);
						}
						blocks[x, y].BG = b;
						blocks[x, y].BGp = pl.Id;
						#endregion
					} else return;

					block_msg = Message.Create("b", l, x, y, b);
					#endregion
#endif
				} else if (b == 1000) {
					#region Text
					if (b == id || m.Count != 5 || l != 0)
						return;
					if (!pl.isModerator && !pl.isOwner && !W_allowText)
						return;

					string text = info.check_Censored(m.GetString(4));
					if (text.Length < 1 || string.IsNullOrWhiteSpace(text))
						return;

					if (text.Length > 150)
						text = text.Remove(150);

					int arg3 = -2,
						aIndex = -2;
					bool isLimit = false;
					// Fit empty slot
					for (int i = 0; i < modText.Length; i++) {
						if (!string.IsNullOrEmpty(modText[i])) {
							if (modText[i] == text) {
								arg3 = i;
								break;
							}
						} else aIndex = i;
					}
					if (arg3 < 0) {
						if (aIndex < 0) {
							if (modText.Length + 51 <= 200) {
								arg3 = modText.Length;
								Array.Resize(ref modText, modText.Length + 50);
								modText[arg3] = text;
							} else isLimit = true;
						} else {
							modText[aIndex] = text;
							arg3 = aIndex;
						}
					}

					if (isLimit) {
						if (pl.system_messages < sys_msg_max) {
							pl.Send("write", SYS, "Fatal error: Reached text limit");
							pl.system_messages++;
						}
						return;
					}

					removeOldBlock(x, y, id, org3);
					int gid = BlockToSPId(b);
					if (FSblock[gid, arg3] == null)
						FSblock[gid, arg3] = new Block();

					FSblock[gid, arg3].Set(x, y);
					blocks[x, y].FG = b;
					blocks[x, y].FGp = pl.Id;
					blocks[x, y].arg3 = arg3;
					blocks[x, y].pId = 0;
					blocks[x, y].pTarget = 0;

					block_msg = Message.Create("lb", x, y, b, text);
					#endregion
				} else if (b == 43 || b == 77 /*|| b == 83*/) {
					#region Coin doors, Music blocks
					if (m.Count != 5 || l != 0)
						return;

					int arg3 = m.GetInt(4);
					if (b == id && arg3 == org3)
						return;

					bool valid = (b == 43) ? pl.isOwner : true;
					if (arg3 < 0 || arg3 >= 100 || !valid)
						return;

					int bid = BlockToSPId(b);
					removeOldBlock(x, y, id, org3);
					if (FSblock[bid, arg3] == null)
						FSblock[bid, arg3] = new Block();

					FSblock[bid, arg3].Set(x, y);
					blocks[x, y].FG = b;
					blocks[x, y].FGp = pl.Id;
					blocks[x, y].arg3 = arg3;
					blocks[x, y].pId = 0;
					blocks[x, y].pTarget = 0;

					block_msg = Message.Create((b == 43) ? "bc" : "bs", x, y, b, arg3);
					#endregion
				} else if (b == 242 && (pl.isOwner || pl.isModerator)) {
					#region Portals
					if (m.Count != 7 || l != 0)
						return;

					int rotation = m.GetInt(4),
						pId = m.GetInt(5),
						pTarget = m.GetInt(6);
					if (pId < 0 || pId >= 100 || pTarget < 0 || pTarget >= 100) return;

					if (rotation >= 4)
						rotation = 0;

					removeOldBlock(x, y, id, org3);
					if (PBlock[rotation, pId, pTarget] == null)
						PBlock[rotation, pId, pTarget] = new Block();

					PBlock[rotation, pId, pTarget].Set(x, y);
					blocks[x, y].FG = b;
					blocks[x, y].FGp = pl.Id;
					blocks[x, y].arg3 = rotation;
					blocks[x, y].pId = pId;
					blocks[x, y].pTarget = pTarget;

					block_msg = Message.Create("pt", x, y, b, rotation, pId, pTarget);
					#endregion
				}

				if (block_msg == null)
					return;

				W_gotEdited = true;
				W_Bcount++;
				Message bot_msg = block_msg;
				bot_msg.Add(pl.Id);
				foreach (Player p in Players) {
					if (p.isInited)
						p.Send(p.isBot ? bot_msg : block_msg);
				}
				return;
			}
			#endregion

			#region cb - Codeblock
			if (m.Type == "cb") {
				if (!pl.canEdit && pl.moved > 0 && !pl.isGod && !pl.isMod) {
					if (getBlock(0, m.GetInt(0), m.GetInt(1)) == 103) {
						pl.canEdit = true;
						pl.Send("access");
					}
				}
				return;
			}
			#endregion

			#region cp - Checkpoint
			if (m.Type == "cp") {
				int x = m.GetInt(0),
					y = m.GetInt(1);
				if ((pl.cPointX != x || pl.cPointY != y) && !pl.isGod && !pl.isMod) {
					if (getBlock(0, x, y) == 104) {
						pl.cPointX = x;
						pl.cPointY = y;
					}
				}
				return;
			}
			#endregion

			if (m.Type == "th") {
				if (!pl.isGod && !pl.isMod && !pl.isDead && !kill_active) {
					pl.isDead = true;
				}
				return;
			}
			#region complete - Trophy
			if (m.Type == "complete") {
				if (!pl.isGod && !pl.isMod && !pl.levelComplete) {
					if (getBlock(0, m.GetInt(0), m.GetInt(1)) == 106) {
						pl.levelComplete = true;
						Broadcast("write", SYS, pl.Name.ToUpper() + " completed this world!");
						pl.Send("info", "Congratulations!", "You completed the world:\n" + W_title);
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
				parseSpawns();
				COOR c = get_next_spawn();
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;
				Broadcast("tele", true, pl.Id, pl.posX, pl.posY);
				return;
			}
			#endregion

			if (m.Type == "diamondtouch") {
				if (m.Count >= 2 && !pl.isGod && !pl.isMod && pl.Face != 31) {
					if (getBlock(0, m.GetInt(0), m.GetInt(1)) == 241) {
						Broadcast("face", pl.Id, 31);
						pl.Face = 31;
					}
				}
				return;
			}
		}

		void GamePlayFunc(Player pl, Message m) {
			if (m.Type == "god" && pl.canEdit && m.Count == 1) {
				if (!W_isOpen || pl.isOwner || pl.isModerator) {
					Broadcast("god", pl.Id, m.GetBoolean(0));
					pl.isGod = m.GetBoolean(0);
				}
				return;
			}
			if (m.Type == "mod") {
				if (!hasAccess(pl, Rights.Moderator)) return;
				pl.isMod = m.GetBoolean(0);
				if (!pl.canEdit) {
					pl.Send("access");
					pl.canEdit = true;
				}
				Broadcast("mod", pl.Id);
				return;
			}

			#region Change face
			if (m.Type == (W_rot13 + "f")) {
				if (pl.firstFace) {
					pl.firstFace = false;
					Broadcast("face", pl.Id, pl.Face);
					return;
				}

				int f = m.GetInt(0);
				if (f != pl.Face) {
#if INDEV
					if(f >= 0){
#else
					// Disallow unknown smilies
					if (f >= 0 && f < (int)C.SMILIES) {
#endif
						Broadcast("face", pl.Id, f);
						pl.Face = f;
					}
				}
				return;
			}
			#endregion

			if (m.Type == (W_rot13 + "k")) {
				if (!pl.isGod && !pl.isMod && !pl.isDead) {
					W_crownC = true;
					W_crown = pl.Id;
				}
				return;
			}

			#region c - Coin
			if (m.Type == "c") {
				if (W_isLoading || m.Count != 3) return;
				if (getBlock(0, m.GetInt(1), m.GetInt(2)) != 100) {
					pl.mWarns += 2;
					return;
				}

				pl.coins = m.GetInt(0);
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
						Broadcast("hide", key_colors[i]);
					} else if (keys[i] > 4) {
						keys[i] = 1;
					}
				}
			}
			#endregion
		}

		void OwnerInteract(Player pl, Message m) {
			#region key - Change key
			if (m.Type == "key") {
				W_key = m.GetString(0);
				Broadcast("lostaccess");
				foreach (Player p in Players) {
					if (!p.isOwner) {
						p.code_tries = 0;
						p.isGod = false;
						p.canEdit = false;
					} else if(p.isGod) // Ugly jump fix
						Broadcast("god", p.Id, true);
				}
				addLog(pl.Name, "Changed code to " + W_key);
				RoomData["needskey"] = "yup";
				RoomData.Save();
				return;
			}
			#endregion

			#region name - Change title
			if (m.Type == "name") {
				W_title = m.GetString(0);
				if (W_title.Length > 60) {
					W_title = W_title.Remove(60, W_title.Length - 60);
				}
				addLog(pl.Name, "Changed title");
				Broadcast("updatemeta", W_Owner, W_title, W_plays);
				RoomData["name"] = W_title;
				RoomData.Save();
				return;
			}
			#endregion

			if (m.Type == "clear" && !W_isLoading) {
				broadcast_clear_world();
				respawn_players(true);
				addLog(pl.Name, "Cleared world");
				return;
			}
			if (m.Type == "save" && !W_isLoading) {
				if (W_gotEdited && W_can_save) {
					W_can_save = false;
					W_isLoading = true;
					addLog(pl.Name, "Saved world");
					save_worlddata(pl);
				}
				// Prevent from mass-save
				W_gotEdited = false;
				return;
			}
		}

		void PlayerInteract(Player pl, Message m) {
			if (m.Type == "say") {
				string msg = m.GetString(0);
				if (msg.Length == 0) return;

				if (msg[0] == '/') {
					#region header
					string[] tmp_c = msg.Split(' ');
					int length = 0;
					string[] args = new string[20];
					for (int i = 0; i < tmp_c.Length; i++) {
						if (tmp_c[i] != "") {
							args[length] = tmp_c[i];
							length++;
						}
					}
					for (int i = length; i < args.Length; i++) {
						args[i] = "";
					}
					tmp_c = null;
					#endregion

					#region /commands
					if (args[0] == "/reset") {
						if (!hasAccess(pl, Rights.Admin)) return;
						if (!W_isLoading) {
							respawn_players(true);
							addLog(pl.Name, "Reset players");
						}
						return;
					}
					if (args[0] == "/clear") {
						if (!hasAccess(pl, Rights.Admin)) return;
						if (!W_isLoading) {
							broadcast_clear_world();
							respawn_players(true);
							addLog(pl.Name, "Cleared world");
						}
						return;
					}
					#region resize
					if (args[0] == "/resize_this_world" && length == 1 && !W_isOpen) {
						if (!hasAccess(pl, Rights.Owner)) return;
						#region resize1
						PlayerIO.BigDB.Load("Worlds", RoomId, delegate(DatabaseObject w_obj) {
							PlayerIO.BigDB.Load("PlayerObjects", w_obj.GetString("owner"), delegate(DatabaseObject o) {
								string[] types = o.GetString("roomType").Split(','),
									ids = o.GetString("roomId").Split(',');

								for (int i = 0; i < ids.Length; i++) {
									if (ids[i] == RoomId) {
										string typestring = types[i];
										if (typestring == "beta0" || typestring == "beta1") {
											W_type = 3;
										} else W_type = info.getInt(typestring.Split('x')[0]);
										break;
									}
								}
								if (W_type < 0) {
									pl.Send("write", "* RESIZER", "Something strange happened.. contact a moderator please.");
									return;
								}
								int[] newSize = info.getWorldSize(W_type);
								int diff_x = W_width - newSize[0],
									diff_y = W_height - newSize[1];
								if (diff_x == 0 && diff_y == 0) {
									W_type = -1;
									pl.Send("write", "* RESIZER", "Not required to resize this world.");
									return;
								}
								pl.Send("write", "* RESIZER", "Please note: With this action, you can destroy parts of your world!");
								pl.Send("write", "* RESIZER", "Old size: " + W_width + "x" + W_height);
								pl.Send("write", "* RESIZER", "New size: " + newSize[0] + "x" + newSize[1] + " (Diff: " + diff_x + "x" + diff_y + ")");
								pl.Send("write", "* RESIZER", "Say '" + args[0] + " " + W_rot13 + "' to resize this world.");
							});
						});
						return;
						#endregion
					}
					if (args[0] == "/resize_this_world" && length > 1) {
						if (!hasAccess(pl, Rights.Owner)) return;
						#region resize2
						if (W_type < 0) {
							pl.Send("write", "* RESIZER", "Say '" + args[0] + "' to see what changes.");
							return;
						}
						if (args[1] != W_rot13) {
							pl.Send("write", "* RESIZER", "Invalid argument. STOP.");
							return;
						}

						int[] newSize = info.getWorldSize(W_type);
						if ((W_width - newSize[0]) == 0 && (W_height - newSize[1]) == 0) {
							pl.Send("write", "* RESIZER", "Not required to resize this world.");
							return;
						}

						PlayerIO.BigDB.Load("Worlds", RoomId, delegate(DatabaseObject w_obj) {
							W_width = newSize[0];
							W_height = newSize[1];
							w_obj.Set("width", W_width);
							w_obj.Set("height", W_height);
							w_obj.Save();
							save_worlddata(pl, true);
						});
						#endregion
						return;
					}
					#endregion
					if (args[0] == "/kick") {
						if (!hasAccess(pl, Rights.Vigilant, length > 1)) return;
						#region kick
						args[1] = args[1].ToLower();
						bool found = false;
						string content = "Tsk. Tsk.";
						if (length > 2) {
							content = "";
							for (int i = 2; i < length; i++) {
								content += args[i] + " ";
							}
						}

						Rights rights = get_rights(pl);
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (get_rights(p) >= rights) break;

								found = true;
								p.Send("info", "You got kicked by " + pl.Name, content);
								p.Disconnect();
							}
						}
						if (found) {
							Broadcast("write", SYS, pl.Name + " kicked " + args[1].ToUpper() + ": " + content);
						} else pl.Send("write", SYS, "Unknown username or player is the owner or a moderator");
						#endregion
						return;
					}
					if (args[0] == "/giveedit") {
						if (!hasAccess(pl, Rights.Admin, length >= 2)) return;
						#region giveedit
						bool found = false;
						args[1] = args[1].ToLower();
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.canEdit) {
									found = true;
									p.canEdit = true;
									p.Send("access");
									p.Send("write", SYS, "You can now edit this world.");
								}
							}
						}
						if (found) {
							addLog(pl.Name, "[+] edit: " + args[1].ToUpper());
							pl.Send("write", SYS, args[1].ToUpper() + " can now edit this world");
						} else pl.Send("write", SYS, "Unknown username or player already has edit");
						#endregion
						return;
					}
					if (args[0] == "/removeedit") {
						if (!hasAccess(pl, Rights.Admin, length > 1)) return;
						#region removeedit
						args[1] = args[1].ToLower();
						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.isOwner && p.canEdit) {
									found = true;
									p.canEdit = false;
									p.isGod = false;
									Broadcast("god", p.Id, false);
									p.Send("lostaccess");
									p.Send("write", SYS, "You can no longer edit this world.");
								}
							}
						}
						if (found) {
							addLog(pl.Name, "[-] edit: " + args[1].ToUpper());
							pl.Send("write", SYS, args[1].ToUpper() + " can no longer edit this world");
						} else pl.Send("write", SYS, "Unknown username, player is owner or does not have edit.");
						#endregion
						return;
					}
					if (args[0] == "/kill") {
						if (!hasAccess(pl, Rights.Admin, length > 1)) return;
						#region kill player
						bool found = false;
						args[1] = args[1].ToLower();
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.isGod && !p.isMod) {
									p.isDead = true;
									found = true;
								}
							}
						}
						if (!found) pl.Send("write", SYS, "Unknown username or player is god/mod");
						#endregion
						return;
					}
					if (args[0] == "/ban") {
						if (!hasAccess(pl, Rights.Vigilant, length > 1)) return;
						#region banning
						string player_name = args[1].ToLower();
						bool found = false,
							isGuest = false;
						if (player_name.StartsWith("x."))
							player_name = player_name.Remove(0, 2);

						if (player_name.StartsWith("guest-")) {
							player_name = "guest";
							isGuest = true;
						}

						Rights rights = get_rights(pl);
						foreach (Player p in Players) {
							if ((p.Name == player_name ||
									p.Name == "x." + player_name ||
									(isGuest && p.isGuest)
								) && get_rights(p) < rights) {
								p.Send("info", "Banned", "You have been banned from this world.");
								p.Disconnect();
								found = true;
							}
						}
						if (found) {
							banned.Add(player_name);
							Broadcast("write", SYS, pl.Name + " banned " + player_name);
						} else pl.Send("write", SYS, "Unknown username, player is owner or moderator");
						#endregion
						return;
					}
					if (args[0] == "/cmban") {
						if (!pl.isVigilant && !pl.isModerator) return;
						if (length != 3) {
							pl.Send("write", SYS, "Please use " + args[0] + " <player> <hours>");
							return;
						}
						#region banning from EE CM
						string player_name = args[1].ToLower();
						bool found = false,
							time_correct = false;

						long time_now = getMTime();
						float ban_time = info.getInt(args[2]);
						if (ban_time <= 0) {
							ban_time = 0.5f;
						} else if (ban_time > 48) {
							ban_time = 48;
						} else time_correct = true;

						string userId = "";
						foreach (Player p in Players) {
							if (!p.isOwner && !p.isVigilant && !p.isModerator) {
								p.Send("info", "Banned", "This account has been banned from EE CM." +
									"Please wait " + ban_time +
									" hours until your ban expires.");
								p.Disconnect();
								userId = p.ConnectUserId;
								found = true;
							}
						}
						if (!time_correct) pl.Send("write", SYS, "Invalid value of hours. Banned for " + ban_time + " hours");

						if (found) {
							PlayerIO.BigDB.Load("PlayerObjects", userId, delegate(DatabaseObject o) {
								if (o.Contains("banned") && o["banned"] is bool) {
									o.Remove("banned");
								}
								o.Set("banned", time_now + (long)(ban_time * 60 * 60));
								o.Save();
							});
							Broadcast("write", SYS, pl.Name + " banned " + player_name + " from EE CM");
						} else pl.Send("write", SYS, "Unknown username, player is owner, vigilant or moderator");
						#endregion
						return;
					}
					if (args[0] == "/unban") {
						if (!hasAccess(pl, Rights.Vigilant, length > 1)) return;
						#region unbanning
						args[1] = args[1].ToLower();
						if (banned.Contains(args[1])) {
							banned.Remove(args[1]);
							Broadcast("write", SYS, pl.Name + " unbanned " + args[1]);
						} else pl.Send("write", SYS, "This player is not banned.");
						#endregion
						return;
					}
					if (args[0] == "/list") {
						if (!hasAccess(pl, Rights.Normal, length > 1)) return;
						#region list
						args[1] = args[1].ToLower();
						string list = "";
						if (args[1] == "ban" || args[1] == "bans") {
							if (!hasAccess(pl, Rights.Vigilant)) return;
							string[] banned_array = banned.GetData();
							for (int i = 0; i < banned_array.Length; i++) {
								list += banned_array[i] + ", ";
							}
							pl.Send("write", SYS, "List of banned users: " + list);
						} else if (args[1] == "admin" || args[1] == "admins") {
							string[] admins_array = admins.GetData();
							for (int i = 0; i < admins_array.Length; i++) {
								list += admins_array[i] + ", ";
							}
							pl.Send("write", SYS, "List of admins: " + list);
						} else if (args[1] == "mute" || args[1] == "mutes") {
							string[] muted_array = pl.muted.GetData();
							for (int i = 0; i < muted_array.Length; i++) {
								list += muted_array[i] + ", ";
							}
							pl.Send("write", SYS, "All players on your mute list: " + list);
						} else pl.Send("write", SYS, "Unknown argument. Use either ban(s), admin(s) or mute(s).");
						#endregion
						return;
					}
					if (args[0] == "/addadmin") {
						if (!hasAccess(pl, Rights.Owner, length > 1)) return;
						#region addadmin
						bool found = false;
						args[1] = args[1].ToLower();
						foreach (Player p in Players) {
							if (p.Name == args[1] && !p.isOwner) {
								found = true;
								if (!admins.Contains(p.Name)) {
									admins.Add(p.Name);
								}
								p.Send("write", SYS, "You are now an admin of this world. Rejoin to use the features.");
							}
						}
						if (found) {
							pl.Send("write", SYS, args[1].ToUpper() + " is now an admin.");
						} else pl.Send("write", SYS, "Unknown username or player is already admin");
						#endregion
						return;
					}
					if (args[0] == "/rmadmin") {
						if (!hasAccess(pl, Rights.Owner, length > 1)) return;
						#region rmadmin
						bool found = false;
						args[1] = args[1].ToLower();
						if (args[1] != "x." + W_Owner && args[1] != W_Owner) {
							if (admins.Contains(args[1])) {
								found = true;
								admins.Remove(args[1]);
							}
						}
						if (found) {
							foreach (Player p in Players) {
								if (p.Name == args[1]) {
									p.isOwner = false;
									p.Send("write", SYS, "Your admin rights got removed.");
								}
							}
							pl.Send("write", SYS, args[1].ToUpper() + " is no longer an admin.");
						} else pl.Send("write", SYS, "Unknown username or player is already admin");
						#endregion
						return;
					}
					if (args[0] == "/teleport") {
						if (!hasAccess(pl, Rights.Edit, length > 1)) return;
						if (W_isOpen && !pl.isModerator && !pl.isOwner) {
							pl.Send("write", SYS, "You can not teleport in an open world.");
							return;
						}
						#region stalking
						string src = pl.Name,
							dst = "";
						int x = 0,
							y = 0;
						if (length >= 4) {
							// /teleport name X Y
							src = args[1].ToLower();
							x = info.getInt(args[2]);
							y = info.getInt(args[3]);
							if (!hasAccess(pl, Rights.None, isValidCoor(x, y))) return;
						} else if (length == 3) {
							//  /teleport X Y
							x = info.getInt(args[1]);
							y = info.getInt(args[2]);
							if (!isValidCoor(x, y)) {
								// /teleport name name
								src = args[1].ToLower();
								dst = args[2].ToLower();
							}
						} else {
							dst = args[1].ToLower();
						}

						if (src != pl.Name) {
							if (!hasAccess(pl, Rights.Admin)) return;
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
								pl.Send("write", SYS, "Could not find player '" + dst.ToUpper() + "'");
								return;
							}
						}

						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == src) {
								Broadcast("tele", false, p.Id, x, y);
								p.posX = x;
								p.posY = y;
								found = true;
							}
						}

						if (!found) {
							pl.Send("write", SYS, "Could not find player '" + src.ToUpper() + "'");
						}
						#endregion
						return;
					}
					if (args[0] == "/loadlevel") {
						if (!hasAccess(pl, Rights.Admin)) return;
						if (W_isSaved && !W_isLoading) {
							W_isLoading = true;
							addLog(pl.Name, "Loaded world");
							load_worlddata(true);
							foreach (Player p in Players) {
								p.coins = 0;
							}
						}
						return;
					}
					if (args[0] == "/getblockinfo" || args[0] == "/gbi") {
						if (!hasAccess(pl, Rights.Edit)) return;
						pl.getBlockInfo = true;
						pl.Send("write", SYS, "Now, click on the block from which you want the information about.");
						return;
					}
					#region modpower
					if (args[0] == "/info") {
						if (!hasAccess(pl, Rights.Moderator, length > 2)) return;
						string content = "";
						for (int i = 2; i < length; i++) {
							content += args[i] + " ";
						}
						Broadcast("info", "Moderator Message: " + args[1], info.check_Censored(content));
						return;
					}
					if (args[0] == "/write") {
						if (!hasAccess(pl, Rights.Moderator, length > 2)) return;
						#region modwrite
						args[1] = args[1].ToLower();
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
						Broadcast("write", args[1], info.check_Censored(content));
						#endregion
						return;
					}
					if (args[0] == "/code") {
						if (!hasAccess(pl, Rights.Moderator, length > 1)) return;
						OwnerInteract(pl, Message.Create("key", args[1]));
						return;
					}
					if (args[0] == "/name") {
						if (!hasAccess(pl, Rights.Moderator, length > 1)) return;
						#region name
						string content = "";
						for (int i = 1; i < length; i++) {
							content += args[i] + " ";
						}
						OwnerInteract(pl, Message.Create("name", content));
						#endregion
						return;
					}
					if (args[0] == "/getip") {
						if (!hasAccess(pl, Rights.Moderator, length > 1)) return;
						#region getIP
						bool found = false;
						args[1] = args[1].ToLower();
						foreach (Player p in Players) {
							if (p.Name == args[1] && !p.isOwner) {
								found = true;
								pl.Send("write", SYS, args[1] + "'s IP: " + p.IPAddress.ToString());
								break;
							}
						}
						if (!found) pl.Send("write", SYS, "Unknown username");
						#endregion
						return;
					}
					if (args[0] == "/eliminate") {
						if (!hasAccess(pl, Rights.Moderator, length > 1)) return;
						#region kick player
						args[1] = args[1].ToLower();
						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								p.Disconnect();
								found = true;
							}
						}
						if (!found) {
							pl.Send("write", SYS, "Unknown username");
						}
						#endregion
						return;
					}
					#endregion
					if (args[0] == "/killroom") {
						if (!hasAccess(pl, Rights.Owner)) return;
						Broadcast("info", "World Killed", "This world has been killed by " + (pl.isOwner ? "the owner" : " a moderator"));
						foreach (Player p in Players) {
							p.Disconnect();
						}
						foreach (Player p in Players) {
							p.Disconnect();
						}
						return;
					}
					if (args[0] == "/respawn") {
						#region respawn player
						if (length > 1) {
							if (!hasAccess(pl, Rights.Admin)) return;
							args[1] = args[1].ToLower();
						} else args[1] = pl.Name;

						bool found = false;
						parseSpawns();
						foreach (Player p in Players) {
							if (p.Name != args[1]) continue;
							if (p.isGod || p.isMod) continue;
							found = true;
							p.cPointX = -1;
							p.cPointY = -1;
							COOR c = get_next_spawn();
							p.posX = c.x * 16;
							p.posY = c.y * 16;
							Broadcast("tele", false, p.Id, p.posX, p.posY);
						}
						if (!found) pl.Send("write", SYS, "Unknown username or player is god/mod");
						#endregion
						return;
					}
					if (args[0] == "/upgrade") {
						if (!hasAccess(pl, Rights.Moderator)) return;
						W_upgrade = true;
						foreach (Player p in Players) {
							if (!p.isModerator) {
								p.Send("upgrade");
								p.Disconnect();
							}
						}
						RoomData["name"] = "shit";
						RoomData.Save();
						return;
					}
					if (args[0] == "/pm") {
						if (!hasAccess(pl, Rights.Normal, length > 2)) return;
						#region pm
						args[1] = args[1].ToLower();
						bool found = false;
						string content = "";
						for (int i = 2; i < length && content.Length < W_chatLimit; i++) {
							content += args[i] + " ";
						}
						if (content.Length > W_chatLimit) {
							content = content.Remove(W_chatLimit);
						}
						content = info.check_Censored(content);

						handle_spam(pl, content);

						if (pl.sameText > 4 || pl.say_counter > 3) {
							pl.Send("write", SYS, "You try to spam, please be nice!");
							return;
						}

						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								if (!p.muted.Contains(pl.Name)) {
									found = true;
									p.Send("write", "*" + pl.Name.ToUpper(), content);
								}
							}
						}

						if (found) {
							pl.Send("write", "*" + args[1].ToUpper(), content);
						} else {
							pl.Send("write", SYS, "Unknown username or you are on the players mute list.");
						}
						#endregion
						return;
					}
					if (args[0] == "/mute") {
						if (!hasAccess(pl, Rights.Normal, length > 1)) return;
						#region mute
						args[1] = args[1].ToLower();
						if (pl.muted.Contains(args[1])) {
							pl.Send("write", SYS, "This player is already muted.");
							return;
						}
						bool found = false;
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								p.Send("write", SYS, pl.Name.ToUpper() + " muted you.");
								found = true;
							}
						}
						if (found) {
							pl.muted.Add(args[1]);
							pl.Send("write", SYS, "The messages from " + args[1].ToUpper() + " will be invisible for you now.");
						} else pl.Send("write", SYS, "Unknown username");
						#endregion
						return;
					}
					if (args[0] == "/unmute") {
						if (!hasAccess(pl, Rights.Normal, length > 1)) return;
						#region unmute
						args[1] = args[1].ToLower();
						if (!pl.muted.Contains(args[1])) {
							pl.Send("write", SYS, "This player is not muted.");
							return;
						}
						pl.muted.Remove(args[1]);
						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								p.Send("write", SYS, pl.Name.ToUpper() + " unputed you.");
							}
						}
						pl.Send("write", SYS, "The messages from " + args[1].ToUpper() + " will be visible for you again.");
						#endregion
						return;
					}
					if (args[0] == "/texts") {
						if (!hasAccess(pl, Rights.Admin, length > 1)) return;
						#region text
						if (is_yes(args[1]) == !W_allowText) {
							W_allowText = !W_allowText;
							string txt = (W_allowText ? "" : "dis") + "allowed";
							addLog(pl.Name, txt + " texts");
							Broadcast("write", SYS, "Texts are now " + txt);
						} else {
							pl.Send("write", SYS, "Texts are already " + (W_allowText ? "" : "dis") + "allowed");
						}
						#endregion
						return;
					}
					if (args[0] == "/woot") {
						if (!pl.wootGiven) {
							Broadcast("write", SYS, pl.Name.ToUpper() + " gave a woot");
							pl.wootGiven = true;
						} else pl.Send("write", SYS, "You already wooted for this world.");
						return;
					}
					if (args[0] == "/log") {
						if (!hasAccess(pl, Rights.Admin)) return;
						string txt = "World Log: (newest on top)";
						for (int i = 0; i < logbook.Length; i++) {
							txt += '\n' + (i + 1).ToString() + ". " + logbook[i];
						}
						pl.Send("write", SYS, txt);
						return;
					}
					if (args[0] == "/help" && length == 1) {
						#region help
						Rights level = get_rights(pl);
						string lMgr = "Level Managing: /getblockinfo (/gbi)" + (W_isSaved ? ", /list admins" : ""),
							pSpec = "\n\nPlayer specific: /respawn, /woot, /rankof [name], /mute [name], /unmute [name], /list mutes",
							cTool = "\n\nOther tools: /pm [name] [text], /teleport {[name], [x] [y]}";
						if (level >= Rights.Vigilant) {
							lMgr += ", /ban [name], /unban [name], /list bans";
							pSpec += ", /kick [name] [reason]";
						}

						if (level >= Rights.Admin) {
							lMgr += ", /clear, /reset, /log, /texts [on/off], /loadlevel";
							pSpec += ", /kill [name], /giveedit [name], /removeedit [name], /respawn [name]";
							cTool += ", /teleport [name] {[to_name], [x] [y]}";
						}
						if (level >= Rights.Owner) {
							lMgr += ", /resize_this_world, /killroom" + (W_isSaved ? ", /addadmin [name], /rmadmin [name]" : "");
						}
						if (level == Rights.Moderator) {
							lMgr += ", /code [text], /name [text]";
							pSpec += ", /getip [name]";
							cTool += ", /write [name] [text], /info [title] [text]";
						}
						pl.Send("write", SYS, lMgr + pSpec + cTool + "\n\nSee /help [command] for further information.");
						#endregion
						return;
					}
					if (args[0] == "/help" && length > 1) {
						#region detailed help
						string cmd = args[1].ToLower();
						if (cmd.StartsWith("/")) cmd = cmd.Remove(0, 1);
						string ret = "Information to the command '" + cmd + "' not found";
						switch (cmd) {
						case "getblockinfo":
						case "gbi":
							ret = "Gets the block information from the one you click on. [Edit needed]";
							break;
						case "admins":
							ret = "Admins can save, load and clear, they also can access to normal world owner commands.";
							break;
						case "addadmin":
							ret = "Adds a player to the list of admins. [World owner only]";
							break;
						case "rmadmin":
							ret = "Removes a player from the list of admins. [World owner only]";
							break;
						case "resize_this_world":
							ret = "Resizes the current world. [Owner rights needed]";
							break;
						case "clear":
							ret = "Clears the current world. [Admin rights needed]";
							break;
						case "reset":
							ret = "Resets all players to the spawn points. [Admin rights needed]";
							break;
						case "loadlevel":
							ret = "Loads the saved world. [Admin rights needed]";
							break;
						case "log":
							ret = "Returns you a detailed logbook of the world-changes with up to 5 entries.";
							break;
						case "list":
							ret = "Returns you a specific list. Available lists: ban(s), admin(s), mute(s).";
							break;
						case "mute":
							ret = "Mutes a player.";
							break;
						case "unmute":
							ret = "Removes a player from your mute list.";
							break;
						case "pm":
							ret = "Sends a private message to a player.";
							break;
						case "kick":
							ret = "Kicks a player from the world. Use /ban to keep them off. [Vigilant/Admin rights needed]";
							break;
						case "ban":
							ret = "Bans a player until world closes, that player can not join the world anymore. [Admin rights needed]";
							break;
						case "unban":
							ret = "Unbans a banned player. [Admin rights needed]";
							break;
						case "kill":
							ret = "Kills a player - Player gets teleported to the last checkpoint or spawn. [Admin rights needed]";
							break;
						case "giveedit":
							ret = "Gives edit to a player. [Admin rights needed]";
							break;
						case "removeedit":
							ret = "Removes edit from a player. [Admin rights needed]";
							break;
						case "respawn":
							ret = "Respawns you or another player to the spawn point.";
							break;
						case "woot":
							ret = "Gives a woot to the world, they do not save.";
							break;
						case "teleport":
							ret = "Teleport has many diffrent uses: /teleport \n";
							ret += " [to_name] - Teleports you to a player.\n";
							ret += " [name] [to_name] - Teleports a player to another.";
							ret += " [x] [y] - Teleports you to the given coordinates.";
							break;
						case "texts":
							ret = "Allows or disallows putting texts in a world. [Admin rights needed]";
							break;
						case "killroom":
							ret = "Kills the world. [World owner only]";
							break;
						case "getip":
							ret = "Gets the IP of the given player. [Moderators only]";
							break;
						case "write":
							ret = "Writes as a defined player in the world a message. [Moderators only]";
							break;
						case "name":
							ret = "Changes the world name without logbook entry. [Moderators only]";
							break;
						case "info":
							ret = "Shows up an info box. [Moderators only]";
							break;
						case "code":
							ret = "Changes code without logbook entry. [Moderators only]";
							break;
						case "rankof":
							ret = "Gets the rank of a player.";
							break;
						}
						pl.Send("write", SYS, ret);
						#endregion
						return;
					}
					if (args[0] == "/rankof") {
						if (!hasAccess(pl, Rights.Normal, length > 1)) return;

						args[1] = args[1].ToLower();

						foreach (Player p in Players) {
							if (p.Name == args[1]) {
								pl.Send("write", SYS, "Rank of player " + args[1].ToUpper() + ": " + get_rights(p).ToString());
								return;
							}
						}
						pl.Send("write", SYS, "Unknown username.");
						return;
					}
					pl.Send("write", SYS, "Unknown command. See /help for all commands.");
					#endregion
					return;
				}
				if (pl.isGuest)
					return;

				#region Spamfilter
				if (msg.Length > W_chatLimit)
					msg = msg.Remove(W_chatLimit, msg.Length - W_chatLimit);
				
				msg = info.check_Censored(msg);

				handle_spam(pl, msg);

				if (pl.sameText > 6) {
					pl.Send("write", SYS, "Please bring up something new. Your messages seem to repeat");
					return;
				}

				if (pl.say_counter > 3) {
					pl.Send("write", SYS, "You try to spam, please be nice!");
					return;
				}

				bool move_log = true;
				for (int i = 0; i < oldChat0.Length; i++) {
					if (string.IsNullOrEmpty(oldChat0[i])) {
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
					if (!p.isGuest && !p.muted.Contains(pl.Name))
						p.Send("say", pl.Id, msg);
					else
						p.Send("say", pl.Id, "");
				}
				#endregion
				return;
			}
			if (m.Type == "m") {
				#region Movements
				if (m.Count < 8) {
					pl.Disconnect();
					return;
				}

				pl.moved++;
				pl.posX = m.GetInt(0);
				pl.posY = m.GetInt(1);
				pl.speedX = m.GetInt(2);
				pl.speedY = m.GetInt(3);

				int gravityX = m.GetInt(4), //gravity
					gravityY = m.GetInt(5),
					keyX = m.GetInt(6), //key press
					keyY = m.GetInt(7);
				pl.gravityX = gravityX;
				pl.gravityY = gravityY;
				pl.keyX = keyX;
				pl.keyY = keyY;

				int x = (int)Math.Round(pl.posX / 16.0),
					y = (int)Math.Round(pl.posY / 16.0);
				if (!isValidCoor(x, y)) {
					pl.Disconnect();
					return;
				}

				if (W_isLoading || pl.moved > moveLimit)
					return;
				
				if (pl.mWarns >= 25) {
					pl.Send("info", "Error", "The data from your client is not correct.");
					pl.Disconnect();
					return;
				} else if (pl.mWarns > -10)
					pl.mWarns--;

				bool skip_send = false,
					has_gravity = !pl.isGod && !pl.isMod && !pl.isOwner;
				#region anti-cheat
				if (has_gravity) {
					bool valid = false;
					for (sbyte mY = -1; mY <= 1; mY++) {
						for (sbyte mX = -1; mX <= 1; mX++) {
							int bl = getBlock(0, mX + x, mY + y),
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
									if (Contains(gravity0Id, bl)) {
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
								if(!is_liquid)
									skip_send = (((dir == 1 && keyX == 0) || 
										(dir == 2 && keyY == 0)) &&
										pl.speedX == 0 && 
										pl.speedY == 0);
								valid = true;
								break;
							}
						}
						if (valid) break;
					}
					if (!valid) pl.mWarns += 8;
				}

				foreach (Player p in Players) {
					if (p.Id == pl.Id || (skip_send && !p.isBot)) continue;
					p.Send("m", pl.Id, pl.posX, pl.posY, pl.speedX, pl.speedY,
								gravityX, gravityY, keyX, keyY);
				}

				if (pl.speedY < -60 && has_gravity) { //-53
					bool isBoost = false;
					for (byte mY = 0; mY <= 20; mY++) {
						for (sbyte mX = -10; mX <= 10; mX++) {
							if (getBlock(0, x + mX, y + mY) == 116) {
								isBoost = true;
								break;
							}
						}
						if (isBoost) break;
					}
					if (!isBoost) pl.mWarns += 8;
				}
				#endregion
				#endregion
			}
		}

		void Keys_Timer() {
			byte t = 25;
			// 0 = red | 1 = green | 2 = blue

			for (byte i = 0; i < keys.Length; i++) {
				if (keys[i] >= t) {
					Broadcast("show", key_colors[i]);
					keys[i] = 0;
				} else if (keys[i] > 0)
					keys[i]++;
			}

			if (W_crownC && W_crown != -1) {
				W_crownC = false;
				Broadcast("k", W_crown);
			}
			isEditBlocked = (W_Bcount >= 60);
			
			W_Bcount = 0;
		}
		void respawn_players(bool clearCoins) {
			Message msg = Message.Create("tele", clearCoins);
			parseSpawns();

			byte players = 0;
			foreach (Player p in Players) {
				if (clearCoins) {
					p.coins = 0;
					p.cPointX = -1;
					p.cPointY = -1;
				}
				if (p.isGod || p.isMod) continue;
				COOR c = get_next_spawn();

				p.speedX = 0;
				p.speedY = 0;
				p.gravityX = 0;
				p.gravityY = 0;
				p.keyX = 0;
				p.keyY = 0;
				p.posX = c.x * 16;
				p.posY = c.y * 16;
				msg.Add(p.Id, p.posX, p.posY);
				players++;
			}
			if (players > 0) {
				Broadcast(msg);
			}
		}
		void load_worlddata(bool respawn = false, bool init = false) {
			PlayerIO.BigDB.Load("Worlds", RoomId, delegate(DatabaseObject o) {
				bool canLoad = false;
				if (o != null) {
					if (o.ExistsInDatabase) {
						if (o.Contains("owner")) {
							canLoad = true;
						}
					}
				}
				if (!canLoad) {
					clear_world(false);
					return;
				}

				if (init) {
					if (o.Contains("name")) W_title = o.GetString("name");
					if (o.Contains("plays")) W_plays = o.GetInt("plays");
					if (o.Contains("admins")) admins = new pList<string>(o.GetString("admins").Split(','));
					
					W_width = o.GetInt("width");
					W_height = o.GetInt("height");
					
					RoomData["name"] = W_title;
					RoomData["plays"] = W_plays + "";
					RoomData["needskey"] = "yup";
					
					PlayerIO.BigDB.Load("PlayerObjects", o.GetString("owner"), delegate(DatabaseObject b) {
						if (b != null) {
							W_Owner = b.GetString("name");
							W_isSaved = true;
						}
						RoomData["owner"] = W_Owner;
						RoomData["owned"] = "true";
						RoomData.Save();
					});
				}
				clear_world(false, false);
				Message M_init = Message.Create("reset");

				#region fromDB
				DatabaseArray ar = new DatabaseArray(),
					texts = new DatabaseArray();
				if (o.Contains("worlddata")) ar = o.GetArray("worlddata");
				if (o.Contains("text")) texts = o.GetArray("text");

				modText = new string[texts.Count + 10];
				for (int i = 0; i < texts.Count; i++) {
					modText[i] = texts.GetString(i);
				}

				for (int i = 0; i < ar.Count; i++) {
					//l,b,x,y,t/a
					if (!ar.Contains(i)) {
						continue;
					}
					DatabaseObject ob = ar.GetObject(i);
					int l = ob.GetInt("layer");
					int b = ob.GetInt("type");
					int arg3 = -2, pId = -2, pTg = -2;
					bool isPortal = false;
					if (ob.Contains("t")) {
						arg3 = ob.GetInt("t");
					} else if (ob.Contains("a")) {
						arg3 = ob.GetInt("a");
					} else if (ob.Contains("pr")) {
						arg3 = ob.GetInt("pr");
						pId = ob.GetInt("pi");
						pTg = ob.GetInt("pt");
						isPortal = true;
					}
					byte[] pX = ob.GetBytes("x");
					byte[] pY = ob.GetBytes("y");
					int[] x = new int[pX.Length / 2];
					int[] y = new int[pY.Length / 2];
					for (int n = 0; n < x.Length; n++) {
						x[n] = pX[n * 2] << 8 | pX[(n * 2) + 1];
						y[n] = pY[n * 2] << 8 | pY[(n * 2) + 1];
						if (!isValidCoor(x[n], y[n])) continue;
						if (l == 1) {
							blocks[x[n], y[n]].BG = b;
							continue;
						}
						blocks[x[n], y[n]].FG = b;
						if (arg3 < 0) continue;
						blocks[x[n], y[n]].arg3 = arg3;
						if (isPortal) {
							blocks[x[n], y[n]].pId = pId;
							blocks[x[n], y[n]].pTarget = pTg;
						}
					}
					if (l == 1 || !Contains(specialBlocks, b)) {
						if (!init) M_init.Add(b, l, pX, pY);
						Nblock[l][(l == 0 ? b : b - 500)] = new Block(x, y);
						continue;
					}

					if (arg3 > -1) {
						if (isPortal) {
							if (arg3 <= 5 && pId <= 100 && pTg <= 100) {
								if (!init) M_init.Add(b, 0, pX, pY, arg3, pId, pTg);
								PBlock[arg3, pId, pTg] = new Block(x, y);
							}
							continue;
						}
						FSblock[BlockToSPId(b), arg3] = new Block(x, y);

						if (init) continue;
						if (b == 1000 || b == 999) {
							if (modText != null) {
								if (modText[arg3] != null) {
									M_init.Add(b, 0, pX, pY, modText[arg3]);
								}
							}
						} else {
							M_init.Add(b, 0, pX, pY, arg3);
						}
					}

				}
				#endregion

				W_isLoading = false;
				W_gotEdited = false;

				if (!init) Broadcast(M_init);
				if (respawn) respawn_players(true);
			});
		}
#if INDEV
		void save_worlddata(Player pl, bool kick_all = false) {
			pl.Send("info", "Warning", "You can not save a world in the indev mode." +
					"\nKick all: " + kick_all.ToString().ToUpper());
			pl.Send("saved");
		}
#else
		void save_worlddata(Player pl, bool kick_all = false) {
			PlayerIO.BigDB.LoadOrCreate("Worlds", RoomId, delegate(DatabaseObject o) {
				Cleanup_Timer();
				if(kick_all) W_isSaved = false;
				DatabaseArray txt = new DatabaseArray();
				int text_count = 0;
				for (int i = 0; i < modText.Length; i++) {
					if (!string.IsNullOrEmpty(modText[i])) {
						txt.Add(text_count);
						txt.Set(text_count, modText[i]);
						text_count++;
					}
				}

				if (o.Contains("text")) o.Remove("text");
				if (text_count > 0) o.Set("text", txt);

				DatabaseArray ar = new DatabaseArray();
				int index = 0,
					fails = 0;

				#region fore-/background
				for (int l = 0; l < 2; l++) {
					for (int b = (l == 0) ? 1 : 0; b < Nblock[l].Length; b++) {
						if (Nblock[l][b] == null) continue;
						if (Contains(specialBlocks, b) || Nblock[l][b].used < 1) {
							continue;
						}

						int length = Nblock[l][b].posX.Length;
						byte[] bufferX = new byte[length * 2],
							bufferY = new byte[length * 2];

						int count = 0;
						int rb = (l == 0) ? b : b + 500;
						for (int i = 0; i < length; i++) {
							int px = Nblock[l][b].posX[i];
							int py = Nblock[l][b].posY[i];
							if (!isValidCoor(px, py)) continue;
							if (getBlock(l, px, py) != rb) {
								Nblock[l][b].Remove(px, py);
								fails++;
								continue;
							}
							bufferX[(count * 2)] = (byte)(px >> 8);
							bufferX[(count * 2) + 1] = (byte)(px % 256);
							bufferY[(count * 2)] = (byte)(py >> 8);
							bufferY[(count * 2) + 1] = (byte)(py % 256);
							count++;
						}

						if (count == 0) continue;
						Array.Resize(ref bufferX, count * 2);
						Array.Resize(ref bufferY, count * 2);

						DatabaseObject ob = new DatabaseObject();
						ob.Set("layer", l);
						ob.Set("type", rb);
						ob.Set("x", bufferX);
						ob.Set("y", bufferY);
						ar.Add(index);
						ar.Set(index, ob);
						index++;
					}
				}
				#endregion

				#region specialBlocks
				for (int b = 0; b < 5; b++) {
					int sb = SPIdToBlock(b);
					for (int a = 0; a < 200; a++) {
						if (FSblock[b, a] == null) continue;
						if (FSblock[b, a].used < 1) continue;

						int length = FSblock[b, a].posX.Length;
						byte[] bufferX = new byte[length * 2],
							bufferY = new byte[length * 2];

						int count = 0;
						for (int i = 0; i < length; i++) {
							int px = FSblock[b, a].posX[i];
							int py = FSblock[b, a].posY[i];
							if (!isValidCoor(px, py)) continue;
							if (blocks[px, py].FG != sb) {
								FSblock[b, a].Remove(px, py);
								fails++;
								continue;
							}
							bufferX[(count * 2)] = (byte)(px >> 8);
							bufferX[(count * 2) + 1] = (byte)(px % 256);
							bufferY[(count * 2)] = (byte)(py >> 8);
							bufferY[(count * 2) + 1] = (byte)(py % 256);
							count++;
						}

						if (count == 0) continue;
						Array.Resize(ref bufferX, count * 2);
						Array.Resize(ref bufferY, count * 2);

						DatabaseObject ob = new DatabaseObject();
						ob.Set("layer", 0);
						ob.Set("type", sb);
						ob.Set("x", bufferX);
						ob.Set("y", bufferY);
						if (b == 4) {
							for (int i = 0; i < txt.Count; i++) {
								if (txt.GetString(i) == modText[a]) {
									ob.Set("t", i);
									break;
								}
							}
						} else {
							ob.Set("a", a);
						}
						ar.Add(index);
						ar.Set(index, ob);
						index++;
					}
				}
				#endregion

				#region portals
				for (int r = 0; r < 6; r++) {
					for (int g = 0; g < 100; g++) {
						for (int p = 0; p < 100; p++) {
							if (PBlock[r, g, p] == null) continue;
							if (PBlock[r, g, p].used < 1) continue;

							int length = PBlock[r, g, p].posX.Length;
							byte[] bufferX = new byte[length * 2],
								bufferY = new byte[length * 2];

							int count = 0;
							for (int i = 0; i < length; i++) {
								int px = PBlock[r, g, p].posX[i];
								int py = PBlock[r, g, p].posY[i];
								if (!isValidCoor(px, py)) continue;
								if (blocks[px, py].FG != 242) {
									PBlock[r, g, p].Remove(px, py);
									fails++;
									continue;
								}
								bufferX[(count * 2)] = (byte)(px >> 8);
								bufferX[(count * 2) + 1] = (byte)(px % 256);
								bufferY[(count * 2)] = (byte)(py >> 8);
								bufferY[(count * 2) + 1] = (byte)(py % 256);
								count++;
							}

							if (count == 0) continue;
							Array.Resize(ref bufferX, count * 2);
							Array.Resize(ref bufferY, count * 2);

							DatabaseObject ob = new DatabaseObject();
							ob.Set("layer", 0);
							ob.Set("type", 242);
							ob.Set("x", bufferX);
							ob.Set("y", bufferY);
							ob.Set("pr", r);
							ob.Set("pi", g);
							ob.Set("pt", p);
							ar.Add(index);
							ar.Set(index, ob);
							index++;
						}
					}
				}
				#endregion

				if(fails > 0) o.Set("fails", fails);
				o.Set("worlddata", ar);
				o.Save();
				pl.Send("saved", fails);
				W_isLoading = false;
				if (kick_all) {
					W_resized = true;
					foreach (Player p in Players) {
						p.isOwner = false;
						p.canEdit = false;
						p.say_counter = 99;
						p.mWarns = 99;
						p.Send("info", "World changed", "The world dimensions changed. Please rejoin.");
						p.Disconnect();
					}
				}
			});
		}
#endif
		void Cleanup_Timer() {
			Broadcast("updatemeta", W_Owner, W_title, W_plays);

			if (modText == null || FSblock == null)
				return;

			#region Remove unused text from the array
			int b = BlockToSPId(1000);
			if (FSblock[b, 0] == null)
				return;

			bool[] is_used = new bool[modText.Length];
			int length = FSblock.GetLength(1);
			for (int i = 0; i < modText.Length && i < length; i++) {
				if (FSblock[b, i] == null)
					continue;

				if (FSblock[b, i].used < 1)
					continue;
				
				is_used[i] = true;
			}
			for (int i = 0; i < modText.Length; i++) {
				if (!is_used[i] && modText[i] != null)
					modText[i] = null;
			}
			#endregion
		}
		void initPlayers() {
			if (W_isLoading || Nblock == null ||
					FSblock == null || PBlock == null)
				return;
			
			if (!W_isLoading)
				W_can_save = true;

			bool spawns_parsed = false;
			foreach (Player pl in Players) {
				if (pl.say_counter > 6) {
					pl.Send("info", "Getting you off", "You seem to share a lot of knowledge. Not everybody likes the flood of messages.");
					pl.Disconnect();
					continue;
				}
				pl.say_counter = 0;
				pl.system_messages = 0;
				if (pl.moved > moveLimit) {
					foreach (Player p in Players) {
						if (p.Id == pl.Id) continue;
						p.Send("m", pl.Id, pl.posX, pl.posY, pl.speedX, pl.speedY,
									pl.gravityX, pl.gravityY, pl.keyX, pl.keyY);
					}
				}
				pl.moved = 0;

				if (!pl.initWait)
					continue;

				pl.initWait = false;

				#region init
				if (W_Owner == pl.Name || ("x." + W_Owner) == pl.Name ||
					(admins.Contains(pl.Name) && W_isSaved)) {
					pl.isOwner = true;
					pl.canEdit = true;
				}
				if (!spawns_parsed) {
					parseSpawns();
					spawns_parsed = true;
				}
				COOR c = get_next_spawn();
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;

				bool W_isTutorial = false;
				Message M_init = Message.Create("init", W_Owner, W_title, W_plays.ToString(), derot13(W_rot13), pl.Id, pl.posX, pl.posY, pl.Name, pl.canEdit, pl.isOwner, W_width, W_height, W_isTutorial && !pl.isModerator && !pl.isOwner);

				Cleanup_Timer();

				#region fore-/background
				for (int l = 0; l < 2; l++) {
					for (int b = (l == 0) ? 1 : 0; b < Nblock[l].Length; b++) {
						if (Nblock[l][b] == null) continue;
						if (Contains(specialBlocks, b) || Nblock[l][b].used < 1) {
							continue;
						}

						int length = Nblock[l][b].posX.Length;
						byte[] bufferX = new byte[length * 2];
						byte[] bufferY = new byte[length * 2];

						int count = 0;
						int rb = (l == 0) ? b : b + 500;
						for (int i = 0; i < length; i++) {
							int px = Nblock[l][b].posX[i];
							int py = Nblock[l][b].posY[i];
							if (!isValidCoor(px, py)) continue;
							if (getBlock(l, px, py) != rb) {
								Nblock[l][b].Remove(px, py);
								continue;
							}
							bufferX[(count * 2)] = (byte)(px >> 8);
							bufferX[(count * 2) + 1] = (byte)(px % 256);
							bufferY[(count * 2)] = (byte)(py >> 8);
							bufferY[(count * 2) + 1] = (byte)(py % 256);
							count++;
						}

						if (count == 0) continue;
						Array.Resize(ref bufferX, count * 2);
						Array.Resize(ref bufferY, count * 2);

						M_init.Add(rb, l, bufferX, bufferY);
					}
				}
				#endregion

				#region specialBlocks
				for (int b = 0; b < 5; b++) {
					int sb = SPIdToBlock(b);
					for (int a = 0; a < 200; a++) {
						if (FSblock[b, a] == null) continue;
						if (FSblock[b, a].used < 1) continue;

						byte[] bufferX = new byte[FSblock[b, a].posX.Length * 2];
						byte[] bufferY = new byte[FSblock[b, a].posX.Length * 2];

						int count = 0;
						for (int i = 0; i < FSblock[b, a].posX.Length; i++) {
							int px = FSblock[b, a].posX[i];
							int py = FSblock[b, a].posY[i];
							if (!isValidCoor(px, py)) continue;
							if (blocks[px, py].FG != sb) {
								FSblock[b, a].Remove(px, py);
								continue;
							}
							bufferX[(count * 2)] = (byte)(px >> 8);
							bufferX[(count * 2) + 1] = (byte)(px % 256);
							bufferY[(count * 2)] = (byte)(py >> 8);
							bufferY[(count * 2) + 1] = (byte)(py % 256);
							count++;
						}

						if (count == 0) continue;
						Array.Resize(ref bufferX, count * 2);
						Array.Resize(ref bufferY, count * 2);

						if (b == 4) {
							M_init.Add(sb, 0, bufferX, bufferY, (modText[a] != null ? modText[a] : "INTERNAL ERROR"));
						} else M_init.Add(sb, 0, bufferX, bufferY, a);
					}
				}
				#endregion

				#region portals
				for (int r = 0; r < 6; r++) {
					for (int g = 0; g < 100; g++) {
						for (int p = 0; p < 100; p++) {
							if (PBlock[r, g, p] == null) continue;
							if (PBlock[r, g, p].used < 1) continue;

							byte[] bufferX = new byte[PBlock[r, g, p].posX.Length * 2];
							byte[] bufferY = new byte[PBlock[r, g, p].posX.Length * 2];

							int count = 0;
							for (int i = 0; i < PBlock[r, g, p].posX.Length; i++) {
								int px = PBlock[r, g, p].posX[i];
								int py = PBlock[r, g, p].posY[i];
								if (!isValidCoor(px, py)) continue;
								if (blocks[px, py].FG != 242) {
									PBlock[r, g, p].Remove(px, py);
									continue;
								}
								bufferX[(count * 2)] = (byte)(px >> 8);
								bufferX[(count * 2) + 1] = (byte)(px % 256);
								bufferY[(count * 2)] = (byte)(py >> 8);
								bufferY[(count * 2) + 1] = (byte)(py % 256);
								count++;
							}

							if (count == 0) continue;
							Array.Resize(ref bufferX, count * 2);
							Array.Resize(ref bufferY, count * 2);

							M_init.Add(242, 0, bufferX, bufferY, r, g, p);
						}
					}
				}
				#endregion

				#endregion
				pl.Send(M_init);
				if (!pl.isGuest) {
					for (int i = 0; i < oldChat0.Length; i++) {
						if (!string.IsNullOrEmpty(oldChat0[i])) {
							pl.Send("write", oldChat0[i], oldChat1[i]);
						}
					}
				}

				#region things
				foreach (Player p in Players) {
					if (p.Id != pl.Id) {
						pl.Send("add", p.Id, p.Name, p.Face, p.posX, p.posY, p.isGod, p.isMod, !p.isGuest, p.coins);
						p.Send("add", pl.Id, pl.Name, pl.Face, pl.posX, pl.posY, false, false, !pl.isGuest, 0);
					}
				}
				if (keys[0] >= 1) {
					pl.Send("hide", "red");
				}
				if (keys[1] >= 1) {
					pl.Send("hide", "green");
				}
				if (keys[2] >= 1) {
					pl.Send("hide", "blue");
				}
				if (W_crown != -1) {
					pl.Send("k", W_crown);
				}
				#endregion
			}
		}
		void killPlayers() {
			kill_active = true;
			Message msg = Message.Create("tele", false);
			int count = 0;
			parseSpawns();
			foreach (Player pl in Players) {
				if (pl.gotCoin) {
					pl.gotCoin = false;
					Broadcast("c", pl.Id, pl.coins);
				}

				if (!pl.isDead) continue;
				pl.isDead = false;
				if (pl.isGod || pl.isMod) continue;
				#region dead
				COOR c = new COOR();

				if (getBlock(0, pl.cPointX, pl.cPointY) == 104) {
					c.x = pl.cPointX;
					c.y = pl.cPointY;
				} else {
					c = get_next_spawn();
				}

				pl.speedX = 0;
				pl.speedY = 0;
				pl.gravityX = 0;
				pl.gravityY = 0;
				pl.keyX = 0;
				pl.keyY = 0;
				pl.posX = c.x * 16;
				pl.posY = c.y * 16;
				
				msg.Add(pl.Id, pl.posX, pl.posY);
				#endregion
				count++;
			}
			if (count > 0) Broadcast(msg);
			kill_active = false;
		}
		int getBlock(int l, int x, int y) {
			int ret = -99;
			if (isValidCoor(x, y)) {
				if (l == 0) {
					ret = blocks[x, y].FG;
				} else if (l == 1) {
					ret = blocks[x, y].BG;
				}
			}
			return ret;
		}
		
		void parseSpawns() {
			if (Nblock == null || Nblock[0] == null || Nblock[0][255] == null) {
				spawnCoor = new COOR[0];
				return;
			}
			spawnCoor = new COOR[Nblock[0][255].used];
			
			int count = 0;
			for (int i = 0; i < Nblock[0][255].posX.Length; i++) {
				int x = Nblock[0][255].posX[i],
					y = Nblock[0][255].posY[i];
				if (isValidCoor(x, y)) {
					spawnCoor[count].x = x;
					spawnCoor[count].y = y;
					count++;
				}
			}
			if (count < spawnCoor.Length) {
				Array.Resize(ref spawnCoor, count);
			}
		}
		COOR get_next_spawn() {
			COOR c = new COOR();
			c.x = 1;
			c.y = 1;
			if (spawnCoor.Length == 0) return c;

			for (int i = 0; i < 40; i++) {
				cSpawn++;
				if (cSpawn >= spawnCoor.Length) {
					cSpawn = 0;
				}

				if (getBlock(0, spawnCoor[cSpawn].x, spawnCoor[cSpawn].y) == 255) {
					return spawnCoor[cSpawn];
				}
			}
			return c;
		}
		void clear_world(bool createBorder = false, bool loadingDone = true) {
			W_isLoading = true;

			Nblock = new Block[2][];
			Nblock[0] = new Block[(int)C.BLOCK_FG_MAX];
			Nblock[1] = new Block[(int)C.BLOCK_BG_MAX];
			FSblock = new Block[5, 201];
			PBlock = new Block[6, 101, 101];
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

				Nblock[0][9] = new Block(xpos, ypos);
			}
			W_gotEdited = false;

			if (loadingDone) W_isLoading = false;
		}
		void broadcast_clear_world() {
			clear_world(true);
			foreach (Player p in Players) {
				if (p.coins > 0) p.gotCoin = true;
				p.coins = 0;
			}
			Broadcast("clear", W_width, W_height);
		}
		int SPIdToBlock(int id) {
			if (id == 0) id = 43;
			if (id == 1) id = 77;
			if (id == 2) id = 83;
			if (id == 3) id = 242;
			if (id == 4) id = 1000;
			return id;
		}
		int BlockToSPId(int id) {
			if (id == 43) id = 0;
			if (id == 77) id = 1;
			if (id == 83) id = 2;
			if (id == 242) id = 3;
			if (id == 1000) id = 4;
			return id;
		}
		void addLog(string p, string s) {
			for (int i = logbook.Length - 1; i > 0; i--) {
				logbook[i] = logbook[i - 1];
			}
			logbook[0] = p.ToUpper() + ' ' + s;
		}
		void removeOldBlock(int x, int y, int id, int arg3) {
			int specialId = BlockToSPId(id);
			if (id == 242) {
				int oldId = blocks[x, y].pId;
				int oldTg = blocks[x, y].pTarget;
				if (PBlock[arg3, oldId, oldTg] != null) {
					PBlock[arg3, oldId, oldTg].Remove(x, y);
				}
				return;
			}
			if (Contains(specialBlocks, id)) {
				if (FSblock[specialId, arg3] != null) {
					FSblock[specialId, arg3].Remove(x, y);
				}
			} else if (Nblock[0][id] != null) {
				Nblock[0][id].Remove(x, y);
			}
		}

		bool hasAccess(Player p, Rights level, bool syntax = true) {
			bool allowed = false;
			string priv = "";
			if (level == Rights.Edit) {
				allowed = p.canEdit;
				priv = "Edit access";
			} else {
				allowed = get_rights(p) >= level;
				priv = level.ToString();
			}
			if (p.system_messages < sys_msg_max) {
				if (!allowed) {
					string txt = "You have insufficient powers for this command.";
					if (priv != "") {
						txt += "\nRequired power level: " + priv;
					}
					p.Send("write", SYS, txt);
				} else if (!syntax) {
					p.Send("write", SYS, "Syntax error. See /help [command] for further information.");
				}
			}
			p.system_messages++;
			return allowed && syntax;
		}
		Rights get_rights(Player p) {
			if (p.isModerator) return Rights.Moderator;
			if (p.Name == W_Owner || p.Name == "x." + W_Owner) return Rights.Owner;
			if (p.isOwner) return Rights.Admin;
			if (p.isVigilant) return Rights.Vigilant;
			if (!p.isGuest) return Rights.Normal;
			return Rights.None;
		}
		void handle_spam(Player pl, string msg) {
			short percent = isEqualTo(msg, pl.last_said);
			pl.say_counter++;
			pl.last_said = msg;

			if (pl.isBot)
				percent -= 20;

			if (percent < 50) {
				if(pl.sameText > 1)
					pl.sameText--;
				return;
			}

			while (percent >= 50) {
				pl.sameText++;
				percent -= 20;
			}
		}

		bool isValidCoor(int x, int y) {
			return (x >= 0 && y >= 0 && x < W_width && y < W_height);
		}
		string generate_rot13() {
			char[] buffer = new char[3];
			string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvw";

			for (int i = 0; i < 3; i++) {
				buffer[i] = chars[info.random.Next(chars.Length)];
			}

			return "." + new string(buffer);
		}
		string derot13(string arg1) {
			int num = 0;
			string str = "";
			for (int i = 0; i < arg1.Length; i++) {
				num = arg1[i];
				if ((num >= 0x61) && (num <= 0x7a)) {
					if (num > 0x6d) num -= 13; else num += 13;
				} else if ((num >= 0x41) && (num <= 90)) {
					if (num > 0x4d) num -= 13; else num += 13;
				}
				str += ((char)num);
			}
			return str;
		}
		short isEqualTo(string text_1, string text_2) {
			if (text_1.Length < 2 || text_2.Length < 2)
				return 60;

			char[] raw_1 = text_1.ToLower().ToCharArray(),
					raw_2 = text_2.ToLower().ToCharArray();

			#region normalize
			for (int i = 0; i < raw_1.Length; i++) {
				char cur = raw_1[i];
				bool found = false;
				for (int k = 0; k < say_normal.Length; k++) {
					if(cur == say_normal[k]){
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
					if(cur == say_normal[k]){
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
				if(raw_1[i] == last_raw_1) continue;
				last_raw_1 = raw_1[i];
				total++;

				bool found = false;
				for (int k = 0; k < raw_2.Length && i < raw_1.Length; k++) {
					if((raw_2[k] == last_raw_2) ||
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
				return (short)((equals / (float)total) * 100 + 0.5);
			} else return 100;
		}
		bool is_yes(string r) {
			bool isyes = false;
			switch (r.ToLower()) {
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
		long getMTime() {
			TimeSpan t = (DateTime.Now - new DateTime(2014, 1, 1));
			return (long)t.TotalSeconds;
		}
		
		bool Contains(int[] arr, int n) {
			for (byte i = 0; i < arr.Length; i++) {
				if (arr[i] == n) return true;
			}
			return false;
		}
	}
}