using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ElevatorsInSpecialRooms
{
	[BepInPlugin(guid, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	public class Plugin : BaseUnityPlugin
	{
		const string guid = "pixelguy.pixelmodding.baldiplus.elevatorsinspecialrooms";

		internal static ConfigEntry<bool> allowMultipleElevatorsInSameSpot, allowElevatorsEverywhere;

		private void Awake()
		{
			allowMultipleElevatorsInSameSpot = Config.Bind("Elevator Spawn Settings", "Allow multiple elevators in special rooms", true, "If True, more than 1 elevator can appear in the same special room.");
			allowElevatorsEverywhere = Config.Bind("Elevator Spawn Settings", "Spawn elevators everywhere", false, "If True, elevators can spawn in any position of the map, instead of just the border.");

			Harmony h = new(guid);
			h.PatchAll();
		}

		internal static void AddNonAllowedRoomFunction(System.Type roomFunc) =>
			nonAllowedRoomFuncTypes.Add(roomFunc);
		internal static bool IsRoomFuncAllowedToReInitialize(System.Type roomFunc) =>
			!nonAllowedRoomFuncTypes.Contains(roomFunc);


		internal static HashSet<System.Type> prohibitedFunctions = [
			typeof(SkyboxRoomFunction),
			typeof(SilenceRoomFunction)
			];

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
		//	__instance.ld.minSpecialRooms = 1;
		//	__instance.ld.maxSpecialRooms = 2;
		//	__instance.ld.specialRoomsStickToEdge = true; // seed 687 for Times to test
		//}

		[HarmonyPatch("RoomFits")]
		[HarmonyPostfix]
		static void DoesElevatorsFit(LevelBuilder __instance, RoomAsset roomAsset, IntVector2 position, ref Direction direction, ref bool __result)
		{

			if (!__result || roomAsset != __instance.ld.elevatorRoom)
				return;
			//Debug.Log("----- CHECKING ELEVATOR FOR DIRECTION " + direction + " -----");

			foreach (CellData cellData in roomAsset.cells)
			{
				var actualPos = position + cellData.pos.Adjusted(roomAsset.potentialDoorPositions[0], direction) + direction.ToIntVector2();
				var cell = __instance.Ec.CellFromPosition(actualPos);
				var room = cell.room;

				//Debug.Log($"Positions checked: {actualPos.ToString()} and room ({(room.name)})");

				if (room.type != RoomType.Room)
					continue;

				if (cell.hideFromMap ||
					!room.potentialDoorPositions.Contains(actualPos)) // It should be potential doors, why did I put entity safe cells lmao
				{
					//Debug.Log("Invalid position to be in");
					__result = false;
					return;
				}
			}

		}


		[HarmonyPatch("LoadRoom", [typeof(RoomAsset), typeof(IntVector2), typeof(IntVector2), typeof(Direction), typeof(bool), typeof(Texture2D), typeof(Texture2D), typeof(Texture2D)])] // that's a method with a LOT of parameters
		[HarmonyPostfix]
		static void SpecialRoomHasExits(RoomController __result) =>
			__result.acceptsExits = __result.category == RoomCategory.Special && (!__result.functions?.functions?.Exists(fun => Plugin.prohibitedFunctions.Contains(fun.GetType())) ?? false);
		// Exists function checks if there isn't any room function that could potentially break the gameplay if an elevator spawned in it

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

			static Vector3 CalculateChildWorldPosition(Vector3 parentPos, Quaternion parentRot, Vector3 childLocalPosition) =>
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

		public static bool IsBitSet(this int flag, int position)
		{
			// Check if the bit at the specified position is set (1)
			return (flag & (1 << position)) != 0;
		}
		public static int ToggleBit(this int flag, int position)
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

	[HarmonyPatch(typeof(LevelGenerator), "Generate", MethodType.Enumerator)]
	static class GeneratorFix
	{
		readonly static System.Type genEnum = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(LevelGenerator), "Generate")).DeclaringType;

		readonly static FieldInfo successField = AccessTools.Field(genEnum, "<success>5__61"),
			connectDirField = AccessTools.Field(genEnum, "<connectDir>5__64"),
			potentialSpawns = AccessTools.Field(genEnum, "<potentialSpawns>5__66"),
			xField = AccessTools.Field(genEnum, "<x>5__59"),
			zField = AccessTools.Field(genEnum, "<z>5__54"),
			valField = AccessTools.Field(genEnum, "<val>5__58");

		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> ComplexTranspiler(IEnumerable<CodeInstruction> i) =>
			new CodeMatcher(i)
			.End() // Fixes an oversight in the code that can lead to a crash (seriously, this has been on since 0.3.8, and hasn't been patched yet)
			.MatchBack(true, // This is the IL code from where the gen checks if the spot is suitable for the elevator
				new(OpCodes.Ldloc_2),
				new(OpCodes.Ldloc_2),
				new(CodeInstruction.LoadField(typeof(LevelBuilder), "ld")),
				new(CodeInstruction.LoadField(typeof(LevelObject), "elevatorRoom")),
				new(OpCodes.Ldnull),
				new(OpCodes.Ldloc_S, name: "V_57"),
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldfld, connectDirField),
				new(CodeInstruction.Call(typeof(Directions), "ToIntVector2", [typeof(Direction)])),
				new(CodeInstruction.Call(typeof(IntVector2), "op_Addition", [typeof(IntVector2), typeof(IntVector2)])),
				new(OpCodes.Ldloc_2),
				new(CodeInstruction.LoadField(typeof(LevelBuilder), "ld")),
				new(CodeInstruction.LoadField(typeof(LevelObject), "elevatorRoom")),
				new(CodeInstruction.LoadField(typeof(RoomAsset), "potentialDoorPositions")),
				new(OpCodes.Ldc_I4_0),
				new(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(List<IntVector2>), "Item")),
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldfld, connectDirField),
				new(CodeInstruction.Call(typeof(Directions), "GetOpposite", [typeof(Direction)])),
				new(CodeInstruction.Call(typeof(LevelBuilder), "RoomFits", [typeof(RoomAsset), typeof(RoomController), typeof(IntVector2), typeof(IntVector2), typeof(Direction)]))
				)
			.MatchForward(false,
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldc_I4_1),
				new(OpCodes.Stfld, successField)
				) // Get to the part I need to change
			.Advance(1)
			.InsertAndAdvance(
				new(OpCodes.Ldfld, potentialSpawns),
				new(OpCodes.Ldloc_2),
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldfld, connectDirField),
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldflda, successField)
				) // Get the stuff

			.SetInstructionAndAdvance(Transpilers.EmitDelegate((List<IntVector2> potentialSpawns, LevelBuilder bld, Direction connectDir, ref bool success) => // Replaces the basic "true" value with a smarter procedure
			{                                                                                                                                                   // This is definitely not the optimal approach, but I cannot just edit the Generator code directly lol
				if (success)
					return;

				IntVector2 offset = connectDir.ToIntVector2();
				for (int i = 0; i < potentialSpawns.Count; i++)
				{
					if (bld.Ec.CellFromPosition(potentialSpawns[i] - offset).room.type != RoomType.Hall) // if there's really a potential spot that is not a hallway, then this boolean should be true
					{
						success = true;
						return;
					}
				}

			}))
			.RemoveInstruction() // Remove the stfld instruction, since this "function" will deal with it (to avoid invalid IL compilation)

			.MatchForward(true, new CodeMatch(CodeInstruction.Call(typeof(LevelBuilder), "CreateElevator", [typeof(IntVector2), typeof(Direction), typeof(Elevator), typeof(RoomAsset), typeof(bool)])))
			.MatchForward(false,
				new(OpCodes.Ldc_I4_0),
				new(CodeInstruction.StoreField(typeof(RoomController), "acceptsExits")))
			.SetInstruction(Transpilers.EmitDelegate(() => Plugin.allowMultipleElevatorsInSameSpot.Value))

			.InstructionEnumeration();

		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> ElevatorsEverywhere(IEnumerable<CodeInstruction> i)
		{
			if (!Plugin.allowElevatorsEverywhere.Value)
				return i;


			var codematcher = new CodeMatcher(i).MatchForward(true,
				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldloc_2),
				new(CodeInstruction.LoadField(typeof(LevelBuilder), "ld")),
				new(CodeInstruction.LoadField(typeof(LevelObject), "exitCount"))
				)
				.MatchForward(false,
				new CodeMatch(OpCodes.Stloc_S, name: "V_56")
				);

			var v56 = codematcher.Instruction.operand;

			codematcher.MatchForward(false, new CodeMatch(OpCodes.Stfld, xField))
				.Advance(1)
				.InsertAndAdvance(
				new(OpCodes.Ldc_I4_1),
				new(OpCodes.Stloc_S, v56), // min X = 1

				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldloc_2),
				new(CodeInstruction.LoadField(typeof(LevelBuilder), "levelSize", true)), // levelSize.x - 1
				new(CodeInstruction.LoadField(typeof(IntVector2), "x")),
				new(OpCodes.Ldc_I4_1),
				new(OpCodes.Sub),
				new(OpCodes.Stfld, valField), // max X

				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldc_I4_1),
				new(OpCodes.Stfld, zField), // min Z = 1

				new(OpCodes.Ldarg_0),
				new(OpCodes.Ldloc_2),
				new(CodeInstruction.LoadField(typeof(LevelBuilder), "levelSize", true)), // levelSize.z - 1
				new(CodeInstruction.LoadField(typeof(IntVector2), "z")),
				new(OpCodes.Ldc_I4_1),
				new(OpCodes.Sub),
				new(OpCodes.Stfld, xField) // max Z
				);


			return codematcher.InstructionEnumeration();
		}
	}
}
