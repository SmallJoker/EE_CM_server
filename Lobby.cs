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

using System;
using PlayerIO.GameLibrary;

namespace EE_CM {
	[RoomType("Lobby3")]
	public class EELGameCode : Game<LobbyPlayer> {
		WorldInfo info = new WorldInfo();

		public override bool AllowUserJoin(LobbyPlayer pl) {
			return PlayerCount < 5;
		}

		public override void UserJoined(LobbyPlayer pl) {
			string ip = pl.IPAddress.ToString();
			if (ip == null) {
					pl.Disconnect();
					return;
			}

			PlayerIO.BigDB.LoadRange("PlayerObjects", "ip", null, ip, ip, 4, delegate(DatabaseObject[] obj) {
				pl.amount_accounts = obj.Length;

				PlayerIO.BigDB.Load("PlayerObjects", pl.ConnectUserId, delegate(DatabaseObject o) {
					if (o == null) return;
					if (!o.ExistsInDatabase) {
						pl.Disconnect();
						return;
					}

					if (!o.Contains("ip") || o.GetString("ip") != ip) {
						o.Set("ip", ip);
						o.Save();
						pl.amount_accounts++;
					}
				});
			});
		}

		public override void GotMessage(LobbyPlayer pl, Message m) {
			if (m.Type == "setUsername") {
				if (pl.amount_accounts >= 3) {
					pl.Send("error", "You already have too many accounts! Clear the flash cache and login.");
					return;
				}
				#region username
				string username = m.GetString(0).ToLower();
				string name_censored = info.check_Censored(username);
				if (username != name_censored) {
					pl.Send("error", "Found unacceptable words.");
					return;
				}

				string allowed_chars = "abcdefghijklmnopqurstuvwxyz1234567890";
				bool allowed = true;
				for (int i = 0; i < username.Length; i++) {
					bool found = false;
					for (int c = 0; c < allowed_chars.Length; c++) {
						if (allowed_chars[c] == username[i]) {
							found = true;
							break;
						}
					}
					if (!found) {
						allowed = false;
						break;
					}
				}
				if (!allowed || username.Length < 3 || username.Length > 20) {
					pl.Send("error", "Please choose a name with 3 to 20 letters and/or numbers.");
					return;
				}
				PlayerIO.BigDB.LoadOrCreate("Usernames", username, delegate(DatabaseObject obj) {
					if (obj.ExistsInDatabase) {
						if (obj.Contains("owner")) {
							pl.Send("error", "Username already taken");
							return;
						}
					}

					obj.Set("owner", pl.ConnectUserId);
					obj.Save();
					pl.GetPlayerObject(delegate(DatabaseObject o) {
						o.Set("name", username);
						o.Set("ip", pl.IPAddress.ToString());
						o.Save();
						pl.Send("username", username);
					});
				});
				#endregion
			}
			if (m.Type == "getShop") {
				#region shop
				int energy = 100, timeToEnergy = 30,
						totalEnergy = 200, secoundsBetweenEnergy = 30;

				pl.Send("getShop", energy, timeToEnergy, totalEnergy, secoundsBetweenEnergy, 0);
				#endregion
			}
			if (m.Type == "useEnergy") {
				pl.Send("useEnergy", "error");
			}
			if (m.Type == "useGems") {
				pl.Send("useGems", "error");
			}
			if (m.Type == "getProfile") {
				pl.Send("getProfile", false);
			}
			if (m.Type == "toggleProfile") {
				pl.Send("toggleProfile", false);
			}
			if (m.Type == "getSavedLevel") {
				if (pl.ConnectUserId == "simpleguest") return;

				#region redirect
				int r_type = m.GetInt(0),
					r_count = m.GetInt(1);
				string typestring = r_type.ToString() + "x" + r_count.ToString();

				if (r_type > (int)C.WORLD_TYPES || r_type < 0 ||
					r_count > (int)C.WORLDS_PER_PLAYER || r_count < 0) {
					pl.Send("r", "OW_invalid_request_" + typestring);
					return;
				}
				
				PlayerIO.BigDB.Load("PlayerObjects", pl.ConnectUserId, delegate(DatabaseObject o) {
					if (!o.ExistsInDatabase) return;

					getWorld(pl, ref o, info.getWorldSize(r_type), typestring);
				}, delegate(PlayerIOError err) {
					throw new Exception("Impossible to load PlayerObject of " + pl.ConnectUserId);
				});
				return;
				#endregion
			}
			if (m.Type == "getRoom" || m.Type == "getBetaRoom") {
				if (pl.ConnectUserId == "simpleguest") return;
				
				#region betarooms
				bool isBetaOnly = (m.Type == "getBetaRoom");
				string betaId = (isBetaOnly ? "beta0" : "beta1");

				PlayerIO.BigDB.Load("PlayerObjects", pl.ConnectUserId, delegate(DatabaseObject o) {
					if (!o.ExistsInDatabase) return;
					
					getWorld(pl, ref o, info.getWorldSize(3), betaId, isBetaOnly);
				}, delegate(PlayerIOError err) {
					throw new Exception("Impossible to load PlayerObject of " + pl.ConnectUserId);
				});
				return;
				#endregion
			}
		}

		void getWorld(LobbyPlayer pl, ref DatabaseObject o, int[] size, string typestring, bool isbeta = false) {
			#region get worldId from DB
			string[] types = new string[0],
				ids = new string[0];

			if (o.Contains("roomType") && o.Contains("roomId")) {
				types = o.GetString("roomType").Split(',');
				ids = o.GetString("roomId").Split(',');
			}

			for (int i = 0; i < types.Length; i++) {
				if (types[i] == typestring) {
					pl.Send("r", ids[i]);
					return;
				}
			}
			#endregion
			
			if (pl.amount_accounts >= 3) {
				pl.Send("r", "OW_limit_reached_" + typestring);
				return;
			}

			#region generate world
			string world_id = (isbeta ? "BW" : "PW") + GenWID(info.random.Next(4, 6)) + "I";
			string p_types = "",
				p_ids = "";

			if (o.Contains("roomType") && o.Contains("roomId")) {
				p_types = o.GetString("roomType");
				p_ids = o.GetString("roomId");
			}

			o.Set("roomType", p_types + typestring + ",");
			o.Set("roomId", p_ids + world_id + ",");
			o.Save();

			DatabaseObject data = new DatabaseObject();
			data.Set("name", "Untitled World");
			data.Set("owner", pl.ConnectUserId);
			data.Set("plays", 0);
			data.Set("width", size[0]);
			data.Set("height", size[1]);
			PlayerIO.BigDB.CreateObject("Worlds", world_id, data, delegate(DatabaseObject obj) {
				pl.Send("r", world_id);
			});
			#endregion
		}

		string GenWID(int size) {
			char[] buffer = new char[size];
			string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvw_0123456789";

			for (int i = 0; i < size; i++) {
				buffer[i] = chars[info.random.Next(chars.Length)];
			}

			return new string(buffer);
		}
	}
}
