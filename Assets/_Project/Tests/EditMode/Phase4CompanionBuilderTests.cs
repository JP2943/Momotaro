using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Editor.Phase4;
using Momotaro.Gameplay.Companion;
using Momotaro.Presentation.Characters;
using Momotaro.Presentation.Companion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：犬丸 Prefab の機械生成（<see cref="Phase4CompanionBuilder"/>）を検証する。生成し直せば必ず同じ構成へ戻ること
    /// （＝手作業の配線漏れが起きないこと）を回帰として固定する。本番パスを汚さないよう一時パスへ生成し、後始末で消す。
    ///
    /// <b>一時パスは 3 つとも渡す。</b>以前は攻撃 Data のパスだけ既定値（本番）に任せており、
    /// 「一時パスへ生成しているつもりのテストが本番の <c>SO_CompanionAttack_Inumaru.asset</c> を触る」状態だった。
    /// テストが出荷物を書き換えると、緑であること自体が信用できなくなる。
    ///
    /// 仮素材（<c>/Placeholder/</c>）が Project に無い環境では意味のある検証ができないため、明示的にスキップする。
    /// </summary>
    public sealed class Phase4CompanionBuilderTests
    {
        private const string TempPrefabPath = "Assets/_Project/Prefabs/Companions/__P4TmpInumaru__.prefab";
        private const string TempDataPath = "Assets/_Project/Data/Companions/__P4TmpCompanion__.asset";
        private const string TempAttackPath = "Assets/_Project/Data/Companions/__P4TmpCompanionAttack__.asset";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.LoadAssetAtPath<Sprite>(Phase4CompanionBuilder.BodySpritePath) == null
                || AssetDatabase.LoadAssetAtPath<Sprite>(Phase4CompanionBuilder.ArrowSpritePath) == null)
            {
                Assert.Ignore("犬丸の仮素材が未 Import のため、Prefab 生成テストをスキップします（"
                    + Phase4CompanionBuilder.BodySpritePath + "）。");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TempPrefabPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPrefabPath);
            }

            if (AssetDatabase.LoadAssetAtPath<CompanionData>(TempDataPath) != null)
            {
                AssetDatabase.DeleteAsset(TempDataPath);
            }

            if (AssetDatabase.LoadAssetAtPath<AttackData>(TempAttackPath) != null)
            {
                AssetDatabase.DeleteAsset(TempAttackPath);
            }
        }

        private static GameObject BuildTemp()
        {
            Phase4CompanionBuilder.BuildResult r =
                Phase4CompanionBuilder.Build(TempPrefabPath, TempDataPath, TempAttackPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Assert.IsNotNull(r.Prefab);
            return r.Prefab;
        }

        [Test]
        public void Build_CreatesDataAndPrefab()
        {
            GameObject prefab = BuildTemp();

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPrefabPath), "Prefab が保存される。");
            var data = AssetDatabase.LoadAssetAtPath<CompanionData>(TempDataPath);
            Assert.IsNotNull(data, "Data が無ければ作成する。");
            Assert.AreEqual("companion_inumaru", data.Id.Value);
            Assert.AreEqual(CompanionRole.Dog, data.Role);
            Assert.IsNotNull(prefab);
        }

        [Test]
        public void Prefab_HasGameplayComponentsWired()
        {
            GameObject prefab = BuildTemp();

            var actor = prefab.GetComponent<CompanionActor>();
            Assert.IsNotNull(actor, "仲間 Actor が付く。");
            Assert.IsNotNull(actor.Data, "Data が配線される。");
            Assert.IsNotNull(prefab.GetComponent<CompanionMotor>(), "移動実行が付く。");
            Assert.IsNotNull(prefab.GetComponent<CompanionFollowController>(), "追従駆動が付く。");

            var body = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "Rigidbody が付く。");
            Assert.IsFalse(body.useGravity, "重力は使わない（接地は固定）。");
            Assert.IsNotNull(prefab.GetComponent<CapsuleCollider>(), "壁で止まるための Collider が付く。");
        }

        [Test]
        public void Prefab_HasPlaceholderVisualsWired()
        {
            GameObject prefab = BuildTemp();

            var presenter = prefab.GetComponent<CompanionPlaceholderPresenter>();
            Assert.IsNotNull(presenter, "仮表示が付く。");
            Assert.IsNotNull(presenter.Actor, "表示対象が配線される。");
            Assert.IsNotNull(presenter.Body, "本体スプライトが配線される。");
            Assert.IsNotNull(presenter.Body.sprite, "本体の仮素材が割り当たる。");
            Assert.IsNotNull(presenter.DirectionArrow, "方向インジケータが配線される。");
            Assert.IsNotNull(presenter.DirectionArrow.sprite, "方向インジケータの仮素材が割り当たる。");
        }

        [Test]
        public void Prefab_KeepsArrowOutsideBillboard()
        {
            GameObject prefab = BuildTemp();

            var billboard = prefab.GetComponentInChildren<CameraFacingBillboard>(true);
            Assert.IsNotNull(billboard, "本体は Billboard 配下（敵・主人公と同じ構成）。");

            var presenter = prefab.GetComponent<CompanionPlaceholderPresenter>();
            Assert.IsTrue(presenter.Body.transform.IsChildOf(billboard.transform), "本体は Billboard の配下。");
            Assert.IsFalse(presenter.DirectionArrow.transform.IsChildOf(billboard.transform),
                "方向インジケータは Billboard の外（回転を受けず足元へ寝かせるため）。");
        }

        [Test]
        public void Prefab_ReferencesPlaceholderAssetsOnly()
        {
            BuildTemp();

            string[] dependencies = AssetDatabase.GetDependencies(TempPrefabPath, true);
            bool hasPlaceholderSprite = false;
            foreach (string path in dependencies)
            {
                if (path.EndsWith(".png"))
                {
                    Assert.IsTrue(path.Contains("/Placeholder/"),
                        "グレーボックス期の素材は /Placeholder/ 配下に置く（P10a の残留検出の前提）: " + path);
                    hasPlaceholderSprite = true;
                }
            }

            Assert.IsTrue(hasPlaceholderSprite, "仮素材を参照している。");
        }

        [Test]
        public void Build_IsRepeatable()
        {
            GameObject first = BuildTemp();
            int firstChildCount = first.transform.childCount;

            GameObject second = BuildTemp();

            Assert.AreEqual(firstChildCount, second.transform.childCount, "再生成しても構成が増殖しない。");
            Assert.IsNotNull(second.GetComponent<CompanionPlaceholderPresenter>().Body);
        }

        [Test]
        public void Build_WritesAttackDataToGivenPath()
        {
            BuildTemp();

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AttackData>(TempAttackPath),
                "攻撃 Data は渡したパスへ作られる。");

            var data = AssetDatabase.LoadAssetAtPath<CompanionData>(TempDataPath);
            Assert.IsNotNull(data.BasicAttack, "通常攻撃が配線される。");
            Assert.AreEqual(TempAttackPath, AssetDatabase.GetAssetPath(data.BasicAttack),
                "配線されるのは渡した一時パスの攻撃 Data（本番のものではない）。");
        }

        /// <summary>
        /// テストが出荷物へ触れないことを固定する。以前は攻撃 Data のパスだけ既定値（本番）に落ちており、
        /// 一時パスへ生成しているつもりの実行が <c>SO_CompanionAttack_Inumaru.asset</c> を作成・保存していた。
        /// テストが出荷物を書き換えると、緑であること自体が信用できなくなる。
        /// </summary>
        [Test]
        public void Build_DoesNotReferenceProductionAssets()
        {
            BuildTemp();

            string[] dependencies = AssetDatabase.GetDependencies(TempPrefabPath, true);
            foreach (string path in dependencies)
            {
                Assert.AreNotEqual(Phase4CompanionBuilder.InumaruDataPath, path,
                    "一時 Prefab が本番の仲間 Data を参照している。");
                Assert.AreNotEqual(Phase4CompanionBuilder.InumaruAttackDataPath, path,
                    "一時 Prefab が本番の攻撃 Data を参照している（出力先の既定値に落ちていないか）。");
                Assert.AreNotEqual(Phase4CompanionBuilder.InumaruPrefabPath, path,
                    "一時 Prefab が本番の Prefab を参照している。");
            }
        }

        /// <summary>
        /// 出力先に既定値を持たせない（＝呼び出し側が省略できない）ことを固定する。
        /// 既定値が戻れば、省略した呼び出しは<b>無言で</b>本番パスへ書き始める。型で塞いでおく。
        /// </summary>
        [Test]
        public void Build_RequiresAllOutputPathsExplicitly()
        {
            MethodInfo build = typeof(Phase4CompanionBuilder).GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(string) }, null);
            Assert.IsNotNull(build, "Build(string, string, string) が公開されている。");

            foreach (ParameterInfo p in build.GetParameters())
            {
                Assert.IsFalse(p.IsOptional,
                    "出力先 " + p.Name + " に既定値を持たせない（省略した呼び出しが本番パスへ書いてしまう）。");
            }
        }

        [Test]
        public void EnsureData_DoesNotOverwriteExisting()
        {
            CompanionData created = Phase4CompanionBuilder.EnsureData(TempDataPath);
            Assert.IsNotNull(created);

            CompanionData again = Phase4CompanionBuilder.EnsureData(TempDataPath);

            Assert.AreSame(created, again, "既存の Data は作り直さない（手で調整した値を失わない）。");
        }
    }
}
