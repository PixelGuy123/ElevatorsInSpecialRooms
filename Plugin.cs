using BepInEx;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.TextEditor;
using UnityEngine.UIElements;

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

		internal static HashSet<System.Type> nonAllowedRoomFuncTypes = [typeof(DetentionRoomFunction)];
	}

	[HarmonyPatch(typeof(LevelBuilder))]
	internal static class ElevatorsInSpecialRooms
	{
		//[HarmonyPatch("Start")]
		//[HarmonyPostfix]
		//static void JustDoIt(LevelBuilder __instance)
		//{
		//	if (!__instance.ld) return;

		//	__instance.ld.exitCount = 4;
		//	__instance.ld.minSpecialRooms = 3;
		//	__instance.ld.maxSpecialRooms = 3;
		//	__instance.ld.specialRoomsStickToEdge = true;
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
				if (__instance.Ec.CellFromPosition(actualPos).room.type == RoomType.Room && (__instance.Ec.CellFromPosition(actualPos).hideFromMap || !__instance.Ec.CellFromPosition(actualPos).room.entitySafeCells.Contains(actualPos)))
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
		static void PreElevatorCreation(IntVector2 pos, ref Direction dir, EnvironmentController ___ec, RoomAsset elevatorRoomAsset, out RoomController __state)
		{
			var dirOffset = dir.ToIntVector2();
			__state = ___ec.CellFromPosition(pos + dirOffset).room;
			if (__state.type == RoomType.Hall)
				return;

			int dirBinPos = dir.GetOpposite().BitPosition();
			var roomZeroPoint = __state.dir switch
			{
				Direction.North => ___ec.RealRoomMin(__state),
				Direction.East => new(___ec.RealRoomMin(__state).x, 0f, ___ec.RealRoomMax(__state).z),
				Direction.South => ___ec.RealRoomMax(__state),
				Direction.West => new(___ec.RealRoomMax(__state).x, 0f, ___ec.RealRoomMin(__state).z),
				_ => Vector3.zero
			};

			for (int i = 0; i < elevatorRoomAsset.cells.Count; i++)
			{
				var frontCell = ___ec.CellFromPosition(pos + elevatorRoomAsset.cells[i].pos.Adjusted(elevatorRoomAsset.potentialDoorPositions[0], dir) + dirOffset); // Offsets from elevator
				if (!frontCell.Null && frontCell.TileMatches(__state) && frontCell.ConstBin.IsBitSet(dirBinPos))
				{
					___ec.CreateCell(frontCell.ConstBin.ToggleBit(dirBinPos), frontCell.room.transform, frontCell.position, frontCell.room);

					int childCount = __state.objectObject.transform.childCount;
					for (int x = 0; x < childCount; x++)
					{
						var trans = __state.objectObject.transform.GetChild(x);
						if (___ec.CellFromPosition(CalculateChildWorldPosition(roomZeroPoint, __state.dir.ToRotation(), trans.localPosition)) == frontCell)
							Object.Destroy(trans.gameObject);
					}
				}
			}


			static Vector3 CalculateChildWorldPosition(Vector3 parentPos, Quaternion parentRot, Vector3 childLocalPosition) => // Chat GPT Shenanigans (simplified into single line) lol (idk quaternions in maths yet)
				parentPos + (parentRot * childLocalPosition);
			
		}

		[HarmonyPatch("CreateElevator")]
		[HarmonyPostfix] // very important, to properly adapt
		static void PostElevatorCreation(ref Direction dir, EnvironmentController ___ec, RoomAsset elevatorRoomAsset, ref RoomController __state)
		{
			if (__state.type == RoomType.Hall)
				return;
			var dirOffset = dir.ToIntVector2();
			var elvSize = elevatorRoomAsset.GetRoomSize();

			__state.size -= new IntVector2(Mathf.Abs(dirOffset.x * elvSize.x), Mathf.Abs(dirOffset.z * elvSize.z));
			//__state.position -= new IntVector2(dirOffset.x * elvSize.x, dirOffset.z * elvSize.z);

			for (int i = 0; i < __state.functions.functions.Count; i++)
			{
				if (Plugin.IsRoomFuncAllowedToReInitialize(__state.functions.functions[i].GetType()))
					__state.functions.functions[i].Initialize(__state);
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
