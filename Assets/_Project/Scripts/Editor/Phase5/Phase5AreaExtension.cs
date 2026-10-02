using System.Collections.Generic;
using Momotaro.Data.World;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// 探索 Scene の中身を<b>設定で足す</b>ための口（P6A。P6 仕様 §11「Builder を設定で拡張し、地形生成器を複製しない」）。
    ///
    /// <see cref="Phase5ExplorationBuilder"/> の A／B の組み立ての途中（地形・既定の仕掛けを置いたあと、
    /// <c>AreaRoot</c> と常駐配線の前）に渡される。拡張側は入口・出入口・仕掛けの一覧へ足したり、
    /// 遭遇戦・普通敵・お地蔵様を置いたりする。P5／P5.5 は拡張を持たないので出力は変わらない。
    /// </summary>
    public sealed class Phase5AreaExtension
    {
        internal Phase5AreaExtension(
            Transform root, Transform environment, Transform fixtureRoot, Transform markers, Transform entries,
            AreaDefinition definition, Phase5BuildTargets targets, Phase5ExplorationBuilder.Fixtures fixtures,
            List<AreaEntryPoint> entryPoints, List<AreaExitGate> exitGates, List<AreaFlagDoor> doors,
            List<AreaTransitionDoor> transitionDoors, List<AreaFlagLever> levers)
        {
            Root = root;
            Environment = environment;
            FixtureRoot = fixtureRoot;
            Markers = markers;
            Entries = entries;
            Definition = definition;
            Targets = targets;
            Fixtures = fixtures;
            EntryPoints = entryPoints ?? new List<AreaEntryPoint>();
            ExitGates = exitGates ?? new List<AreaExitGate>();
            Doors = doors;
            TransitionDoors = transitionDoors;
            Levers = levers;
        }

        /// <summary>Area の根（Area ローカル座標で組む）。</summary>
        public Transform Root { get; }

        /// <summary>地形の親。</summary>
        public Transform Environment { get; }

        /// <summary>仕掛けの親。</summary>
        public Transform FixtureRoot { get; }

        /// <summary>目印の親。</summary>
        public Transform Markers { get; }

        /// <summary>入口の親。</summary>
        public Transform Entries { get; }

        /// <summary>この Area の Data。</summary>
        public AreaDefinition Definition { get; }

        /// <summary>配置の設定。</summary>
        public Phase5BuildTargets Targets { get; }

        internal Phase5ExplorationBuilder.Fixtures Fixtures { get; }

        /// <summary>AreaRoot へ登録する入口（足してよい）。</summary>
        public List<AreaEntryPoint> EntryPoints { get; }

        /// <summary>AreaRoot へ登録する開放出入口（足してよい）。</summary>
        public List<AreaExitGate> ExitGates { get; }

        /// <summary>AreaRoot へ登録する門（null のこともある）。</summary>
        public List<AreaFlagDoor> Doors { get; set; }

        /// <summary>AreaRoot へ登録する扉（null のこともある）。</summary>
        public List<AreaTransitionDoor> TransitionDoors { get; set; }

        /// <summary>AreaRoot へ登録するレバー（null のこともある）。</summary>
        public List<AreaFlagLever> Levers { get; set; }
    }
}
