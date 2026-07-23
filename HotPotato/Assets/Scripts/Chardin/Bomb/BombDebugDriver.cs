using UnityEngine;

namespace Chardin
{
    /// <summary>
    /// 临时调试：Play 后键盘测炸弹。接上 Battle 后可删或关掉。
    /// 1 传 / 2 塞 / 3 拆 / H 切换持有者视角 / R 重置(8–12)
    /// </summary>
    [RequireComponent(typeof(Bomb))]
    public sealed class BombDebugDriver : MonoBehaviour
    {
        [SerializeField] Bomb bomb;
        [SerializeField] bool enableKeyboard = true;
        [SerializeField] Vector2Int resetRange = new Vector2Int(8, 12);

        void Awake()
        {
            if (bomb == null)
                bomb = GetComponent<Bomb>();
        }

        void Start()
        {
            if (!bomb.Logic.IsArmed)
                bomb.Arm(Random.Range(resetRange.x, resetRange.y + 1), viewerIsHolder: true);

            bomb.ActionResolved += OnAction;
            bomb.ExplodedOnSelf += () => Debug.Log("[Bomb] 手滑后自己挨炸！");
        }

        void OnDestroy()
        {
            if (bomb != null)
                bomb.ActionResolved -= OnAction;
        }

        void Update()
        {
            if (!enableKeyboard || bomb == null)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                bomb.Pass();
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                bomb.Shove();
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                bomb.Defuse();
            else if (Input.GetKeyDown(KeyCode.H))
                bomb.SetViewerIsHolder(!bomb.ViewerIsHolder);
            else if (Input.GetKeyDown(KeyCode.R))
                bomb.Arm(Random.Range(resetRange.x, resetRange.y + 1), bomb.ViewerIsHolder);
        }

        void OnAction(BombActionResult result)
        {
            string slip = result.Slipped ? " 手滑!" : "";
            Debug.Log($"[Bomb] {result.Action} -> {result.CountdownAfter}{slip} transfer={result.ShouldTransfer}");
        }
    }
}
