using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ElevatorsInSpecialRooms
{
	[BepInPlugin(guid, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		const string guid = "pixelguy.pixelmodding.baldiplus.elevatorsinspecialrooms";

		private void Awake()
		{
			Harmony h = new(guid);
			h.PatchAll();
		}

		public static void AddProhibitedSpecialRoomPrefix(string prefix) =>
			prohibitedNames.Add(prefix.ToLower());
		internal static bool IsProhibitedSpecialRoomPrefix(string name)
		{
			name = name.ToLower();
			return prohibitedNames.Exists(pre => name.Contains(pre));
		}
		internal static void AddNonAllowedRoomFunction(System.Type roomFunc) =>
			nonAllowedRoomFuncTypes.Add(roomFunc);
		internal static bool IsRoomFuncAllowedToReInitialize(System.Type roomFunc) =>
			!nonAllowedRoomFuncTypes.Contains(roomFunc);

		internal static List<string> prohibitedNames = ["library", "playground"];

		internal static HashSet<System.Type> nonAllowedRoomFuncTypes = [
			typeof(DetentionRoomFunction),
			typeof(NanaPeelRoomFunction),
			typeof(EntityBufferRoomFunction),
			typeof(CharacterPostersRoomFunction),
			typeof(ChalkboardBuilderFunction),
			typeof(CellBlockRoomFunction),
			typeof(CoverRoomFunction),
			typeof(DetentionRoomFunction),
			typeof(FieldTripBaseRoomFunction),
			typeof(FieldTripEntranceRoomFunction),
			typeof(RandomAmbienceRoomFunction),
			typeof(AmbienceRoomFunction),
			typeof(SilenceRoomFunction),
			typeof(StoreRoomFunction)
			];
	}

	[HarmonyPatch(typeof(LevelBuilder))]
	internal static class ElevatorsInSpecialRooms
	{
		//[HarmonyPatch(typeof(EnvironmentController), "SetTileInstantiation")]
		//[HarmonyPostfix]
		//static void ChangeThisToTrue(EnvironmentController __instance) =>
		//	__instance.instantiateTiles = true;

		//[HarmonyPatch("Start")]
		//[HarmonyPostfix]
		//static void JustDoIt(LevelBuilder __instance)
		//{
		//	if (!__instance.ld) return;

		//	__instance.ld.exitCount = 4;
		//	__instance.ld.minSpecialRooms = 2;
		//	__instance.ld.maxSpecialRooms = 2;
		//	__instance.ld.specialRoomsStickToEdge = true;
		//	__instance.ld.potentialSpecialRooms.DoIf(x => x.selection.name.Contains("Cafeteria"), x => x.weight = 99999);
		//}

		[HarmonyPatch("RoomFits")]
		[HarmonyPostfix]
		static void DoesElevatorsFit(LevelBuilder __instance, RoomAsset roomAsset, IntVector2 position, ref Direction direction, ref bool __result)
		{
			if (!__result || roomAsset != __instance.ld.elevatorRoom)
				return;

			foreach (CellData cellData in roomAsset.cells)
			{
				var actualPos = position + cellData.pos.Adjusted(roomAsset.potentialDoorPositions[0], direction) + direction.ToIntVector2();
				if (__instance.Ec.CellFromPosition(actualPos).room &&
					__instance.Ec.CellFromPosition(actualPos).room.type == RoomType.Room &&

					(__instance.Ec.CellFromPosition(actualPos).room.functions?.functions?.Exists(fun => fun is SkyboxRoomFunction) ?? false ||
					__instance.Ec.CellFromPosition(actualPos).hideFromMap ||
					!__instance.Ec.CellFromPosition(actualPos).room.entitySafeCells.Contains(actualPos))
					)
				{
					__result = false;
					return;
				}
			}

		}


		[HarmonyPatch("LoadRoom")]
		[HarmonyPostfix]
		static void SpecialRoomHasExits(RoomController __result) =>
			__result.acceptsExits = __result.category == RoomCategory.Special && !Plugin.IsProhibitedSpecialRoomPrefix(__result.name);

		[HarmonyPatch("CreateElevator")]
		[HarmonyPrefix] // very important, to properly adapt
		static void PreElevatorCreation(IntVector2 pos, ref Direction dir, EnvironmentController ___ec, RoomAsset elevatorRoomAsset, out object[] __state)
		{
			var dirOffset = dir.ToIntVector2();
			var room = ___ec.CellFromPosition(pos + dirOffset).room;
			__state = [room, room.size, room.position];

			if (room.type == RoomType.Hall)
				return;

			int dirBinPos = dir.GetOpposite().BitPosition();
			var roomZeroPoint = room.dir switch
			{
				Direction.North => ___ec.RealRoomMin(room),
				Direction.East => new(___ec.RealRoomMin(room).x, 0f, ___ec.RealRoomMax(room).z),
				Direction.South => ___ec.RealRoomMax(room),
				Direction.West => new(___ec.RealRoomMax(room).x, 0f, ___ec.RealRoomMin(room).z),
				_ => Vector3.zero
			};

			for (int i = 0; i < elevatorRoomAsset.cells.Count; i++)
			{
				var frontCell = ___ec.CellFromPosition(pos + elevatorRoomAsset.cells[i].pos.Adjusted(elevatorRoomAsset.potentialDoorPositions[0], dir) + dirOffset); // Offsets from elevator
				if (!frontCell.Null && frontCell.TileMatches(room) && frontCell.ConstBin.IsBitSet(dirBinPos))
				{
					___ec.CreateCell(frontCell.ConstBin.ToggleBit(dirBinPos), frontCell.room.transform, frontCell.position, frontCell.room);

					int childCount = room.objectObject.transform.childCount;
					for (int x = 0; x < childCount; x++)
					{
						var trans = room.objectObject.transform.GetChild(x);
						if (trans && ___ec.CellFromPosition(CalculateChildWorldPosition(roomZeroPoint, room.dir.ToRotation(), trans.localPosition)) == frontCell)
							Object.Destroy(trans.gameObject);
					}
				}
			}

			static Vector3 CalculateChildWorldPosition(Vector3 parentPos, Quaternion parentRot, Vector3 childLocalPosition) => // Chat GPT Shenanigans (simplified into single line) lol (idk quaternions in maths yet)
				parentPos + (parentRot * childLocalPosition);

		}

		[HarmonyPatch("CreateElevator")]
		[HarmonyPostfix] // very important, to properly adapt
		static void PostElevatorCreation(ref object[] __state)
		{
			RoomController room = (RoomController)__state[0];

			if (room.type == RoomType.Hall)
				return;

			room.size = (IntVector2)__state[1]; // Workaround to force the rooms to still stay in their original sizes and positions
			room.position = (IntVector2)__state[2];

			for (int i = 0; i < room.functions.functions.Count; i++)
			{
				if (Plugin.IsRoomFuncAllowedToReInitialize(room.functions.functions[i].GetType()))
					room.functions.functions[i].Initialize(room);
			}
		}

		public static bool IsBitSet(this int flag, int position) // Thanks ChatGPT
		{
			// Check if the bit at the specified position is set (1)
			return (flag & (1 << position)) != 0;
		}
		public static int ToggleBit(this int flag, int position) // Thanks ChatGPT
		{
			// Use XOR to flip the bit at the specified position
			return flag ^ (1 << position);
		}
		public static IntVector2 GetRoomSize(this RoomAsset asset)
		{
			IntVector2 size = new(0, 0);

			for (int i = 0; i < asset.cells.Count; i++)
			{
				if (asset.cells[i].pos.x > size.x)
					size.x = asset.cells[i].pos.x;

				if (asset.cells[i].pos.z > size.z)
					size.z = asset.cells[i].pos.z;
			}

			return size;
		}
	}
}
