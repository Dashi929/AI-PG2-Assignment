using UnityEngine;
using UnityEngine.AI;

namespace EcsFramework
{

    public class AIComponent : IComponent
    {
        public GameObject go;
        public NavMeshAgent Agent { get; private set; }
        public Animator Animator { get; private set; }

        public string moveAnim = "Walk";
        public string idleAnim = "Idle";
        public float walkSpeed = 3f;
        public bool avoidOthers = true;

        // 该 NPC 每帧运行的行为树
        public BTNode Root { get; private set; }
        public AIContext Context { get; private set; }

        // NPC 当前乘坐的载具（由外部设置）
        public GameObject Vehicle { get; set; }

        // 行为树请求的动画状态（每帧刷新，UpdateAnim 消费）
        private string animRequest;

        // 玩家右键下达的移动命令目的地（世界坐标）。非 null 时优先执行命令，
        // 到达后自动清除并回归日常行为树。
        public Vector3? CommandDestination { get; private set; }

        // 玩家右键下达的闲聊目标（NPC）。非 null 时走向目标并交谈，随后清除。
        public Transform ChatTarget { get; private set; }

        // 玩家右键下达的乘车目标（车辆）。非 null 时走向车辆并上车坐好。
        public CarComponent RideCar { get; private set; }

        // 行为树在乘车时是否继续执行（司机行为树自动开车时需要；手动乘车为 false）
        public bool BehaviorContinuesWhileDriving { get; set; }

        private bool initialized;

         //玩家命令：让该 NPC 前往指定世界坐标。返回是否接受命令。
        public bool CommandMoveTo(Vector3 dest)
        {
            if (!initialized || Agent == null) return false;
            // 命令最高优先：重置行为树（释放司机占用的车等）、清掉旧路径
            if (Root != null) Root.Reset();
            ClearCommand();
            // 乘车时：同时清掉车的旧路径，让新命令能立即打断
            var car = ResolveRideCar();
            if (car != null && car.Agent != null && car.Agent.hasPath)
                car.Agent.ResetPath();
            else if (Agent.hasPath)
                Agent.ResetPath();
            CommandDestination = dest;
            // 接受指令时移动速度加快
            Agent.speed = Mathf.Max(1f, walkSpeed) * 2f;
            return true;
        }

         //玩家命令：让该 NPC 走向车辆并上车坐好。
        public bool CommandRide(CarComponent car)
        {
            if (!initialized || Agent == null || car == null) return false;
            if (Root != null) Root.Reset();
            ClearCommand();
            if (Agent.hasPath) Agent.ResetPath();
            RideCar = car;
            Agent.speed = Mathf.Max(1f, walkSpeed) * 2f;
            return true;
        }

         //解析当前乘的车辆（CarComponent 是 ECS 组件，需从 CarEntity 的 Entity 获取）。
        private CarComponent ResolveRideCar()
        {
            if (Vehicle == null) return null;
            var carEntity = Vehicle.GetComponent<CarEntity>();
            if (carEntity == null) return null;
            var entity = carEntity.Entity;
            if (entity == null || !entity.Has<CarComponent>()) return null;
            return entity.Get<CarComponent>();
        }

         //若正在乘车，立即下车（恢复显示 NPC、恢复 agent）。
        public void ExitVehicle()
        {
            if (Vehicle == null) return;
            var car = ResolveRideCar();
            if (car != null) car.Exit();
            // 下车后恢复自身 agent（乘车时被禁用，SetParent 后可能丢失 NavMesh 绑定）
            if (Agent != null)
            {
                if (!Agent.enabled) Agent.enabled = true;
                // 重新放置到 NavMesh，确保下车后能移动（排除湖泊）
                if (NavMesh.SamplePosition(go.transform.position, out var hit, 10f, NavMesh.AllAreas & ~(1 << 1)))
                {
                    go.transform.position = hit.position;
                    Agent.Warp(hit.position);
                }
                Agent.isStopped = false;
            }
            RideCar = null;
        }

         //玩家命令：让该 NPC 走向目标 NPC 并闲聊（双方面对面、播 Idle、头上气泡）。
        public bool CommandChat(Transform target)
        {
            if (!initialized || Agent == null || target == null) return false;
            if (Root != null) Root.Reset();
            ClearCommand();
            if (Agent.hasPath) Agent.ResetPath();
            ChatTarget = target;
            // 接受指令时移动速度加快（方便玩家观察响应）
            Agent.speed = Mathf.Max(1f, walkSpeed) * 2f;
            return true;
        }

         //清除所有命令，回归日常行为。
        public void CancelCommand()
        {
            ClearCommand();
            if (Agent != null && Agent.hasPath) Agent.ResetPath();
        }

         //解析互动对端的 AI 控制器（支持人类 NPC 和动物）。
        private static AIComponent ResolvePartnerController(Transform target)
        {
            if (target == null) return null;
            var human = target.GetComponent<HumanEntity>();
            if (human != null) return human.GetAIController();
            var animal = target.GetComponent<AnimalEntity>();
            if (animal != null) return animal.GetAIController();
            return null;
        }

        private void ClearCommand()
        {
            // 结束闲聊时通知对方也回到 idle（人类/动物都支持）
            if (ChatTarget != null)
            {
                var partnerCtrl = ResolvePartnerController(ChatTarget);
                if (partnerCtrl != null) partnerCtrl.EndChat();
            }
            CommandDestination = null;
            ChatTarget = null;
            RideCar = null;
            // 恢复日常移动速度
            if (Agent != null && walkSpeed > 0f) Agent.speed = walkSpeed;
        }


        public void Init(GameObject aiGo, BTNode root)
        {
            go = aiGo;
            Agent = go.GetComponent<NavMeshAgent>();
            Animator = go.GetComponent<Animator>();
            Context = new AIContext { Controller = this };
            Root = root;
            initialized = true;

            if (walkSpeed > 0f) Agent.speed = walkSpeed;
            Agent.obstacleAvoidanceType = avoidOthers
                ? ObstacleAvoidanceType.HighQualityObstacleAvoidance
                : ObstacleAvoidanceType.NoObstacleAvoidance;
        }

         //行为树节点调用：请求播放某个动画状态（同帧多次请求时最后者生效）。
        public void RequestAnim(string state)
        {
            if (string.IsNullOrEmpty(state)) return;
            animRequest = state;
        }

         //
        /// 给模型实例添加 Animator 并按模型名加载对应 AnimatorController。
        /// 
        public Animator SetupModelAnimator(GameObject model, string modelName)
        {
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            var ctrl = Resources.Load<RuntimeAnimatorController>("Animations/" + modelName);
            if (ctrl == null) return null;
            anim.runtimeAnimatorController = ctrl;
            Animator = anim;
            return anim;
        }

        public void OnUpdate()
        {
            if (!initialized || Context == null || Agent == null) return;
            Context.DeltaTime = Time.deltaTime;
            Context.TimeOfDay = DayNight.DayNightClock.Hour;

            // 乘车时，禁用该角色自身的 agent，避免它漫游
            // （非乘车时保持 agent 的当前状态——飞行类行为可能需要自行控制）
            bool driving = Vehicle != null;
            if (driving && Agent.enabled) Agent.enabled = false;
            if (driving && Agent.hasPath) Agent.ResetPath();

            animRequest = null;

            // 作为闲聊对象：停下当前行为，只接受发起者设置的朝向/动画
            if (driving && BehaviorContinuesWhileDriving)
            {
                // 司机行为树在乘车时继续执行（如 TaxiRound 自动开车）
                if (CommandDestination.HasValue)
                {
                    UpdateCommand();
                }
                else if (Root != null)
                {
                    Root.Tick(Context);
                }
            }
            else if (driving)
            {
                // 手动乘车：不驱动自身 agent、不 tick 行为树，播 Idle；
                // 若有移动命令则驱动车辆行驶
                if (CommandDestination.HasValue)
                {
                    UpdateCommand();
                }
                else
                {
                    animRequest = "Idle";
                }
            }
            else if (isChatting)
            {
                // 作为闲聊对象：若玩家下发指令，结束闲聊并处理命令
                if (CommandDestination.HasValue || RideCar != null || ChatTarget != null)
                {
                    EndChat();
                    if (Agent.isOnNavMesh) Agent.isStopped = false;
                    // 处理命令
                    if (RideCar != null) UpdateRide();
                    else if (ChatTarget != null) UpdateChat();
                    else if (CommandDestination.HasValue) UpdateCommand();
                }
                else
                {
                    if (Agent.isOnNavMesh)
                    {
                        if (Agent.hasPath) Agent.ResetPath();
                        Agent.isStopped = true;
                    }
                    // 不 tick 行为树
                }
            }
            else
            {
                if (Agent.isOnNavMesh) Agent.isStopped = false;
                // 玩家命令优先：乘车 > 闲聊 > 移动目的地 > 日常行为树
                if (RideCar != null)
                {
                    UpdateRide();
                }
                else if (ChatTarget != null)
                {
                    UpdateChat();
                }
                else if (CommandDestination.HasValue)
                {
                    UpdateCommand();
                }
                else if (Root != null)
                {
                    Root.Tick(Context);
                }
            }

            UpdateAnim();
        }

         //执行玩家命令：朝目的地移动。若正在乘车，则驱动车辆移动。
        private void UpdateCommand()
        {
            Vector3 dest = CommandDestination.Value;

            // 乘车状态：驱动车辆而不是 NPC 本身
            var car = ResolveRideCar();
            if (car != null && car.Agent != null)
            {
                var carAgent = car.Agent;
                if (carAgent.isOnNavMesh && !carAgent.pathPending && (!carAgent.hasPath || carAgent.pathStatus == NavMeshPathStatus.PathInvalid))
                    carAgent.SetDestination(dest);
                animRequest = "Idle";
                if (carAgent.hasPath && !carAgent.pathPending && carAgent.remainingDistance <= 1.5f)
                {
                    carAgent.ResetPath();
                    CancelCommand();
                    animRequest = "Idle";
                }
                return;
            }

            // 普通移动（步行）
            if (Agent.isOnNavMesh && !Agent.pathPending && (!Agent.hasPath || Agent.pathStatus == NavMeshPathStatus.PathInvalid))
            {
                Agent.SetDestination(dest);
            }
            animRequest = "Walk";

            // 到达目的地：清除命令，回归日常行为
            if (Agent.hasPath && !Agent.pathPending && Agent.remainingDistance <= 1.5f)
            {
                Agent.ResetPath();
                CancelCommand();
                animRequest = "Idle";
            }
        }

         //
        /// 乘车命令：走向车辆，靠近后上车坐好（CarComponent.Enter），
        /// 乘车状态播放 Idle。命令持续到玩家取消或再次指令。
        /// 
        private void UpdateRide()
        {
            CarComponent car = RideCar;
            // 车辆被销毁：取消乘车
            if (car == null || car.go == null)
            {
                CancelCommand();
                animRequest = "Idle";
                return;
            }

            float dist = Vector3.Distance(go.transform.position, car.go.transform.position);

            // 1) 还没到车旁：走过去（上车范围放宽，车体较大）
            if (dist > 3.5f)
            {
                if (Agent.isOnNavMesh && (!Agent.hasPath || Agent.pathStatus == NavMeshPathStatus.PathInvalid))
                    Agent.SetDestination(car.go.transform.position);
                animRequest = "Walk";
                return;
            }

            // 2) 到了：上车坐好
            if (Agent.hasPath) Agent.ResetPath();
            if (Vehicle == null)
            {
                // 尝试上车；若被占则等待
                if (!car.Enter(this))
                {
                    animRequest = "Idle";
                    return;
                }
            }
            // 已上车：清空 RideCar，让后续右键移动命令驱动车辆
            RideCar = null;
            animRequest = "Idle";
        }

         //
        /// 闲聊命令：走向目标 NPC，靠近后双方面对面、随机播 emoji。
        /// 闲聊持续直到玩家取消选中或发出新指令。
        /// 
        private void UpdateChat()
        {
            Transform target = ChatTarget;
            // 目标被销毁：取消闲聊
            if (target == null)
            {
                ClearCommand();
                animRequest = "Idle";
                return;
            }

            float dist = Vector3.Distance(go.transform.position, target.position);

            // 1) 还没靠近：走向目标（面向对方），保持 4m 闲聊距离
            if (dist > 4f)
            {
                if (Agent.isOnNavMesh && (!Agent.hasPath || Agent.pathStatus == NavMeshPathStatus.PathInvalid))
                    Agent.SetDestination(target.position);
                animRequest = "Walk";
                return;
            }

            // 2) 足够近：停下来面对面交谈
            if (Agent.hasPath) Agent.ResetPath();
            var dir = target.position - go.transform.position;
            FaceDirection(dir);

            // 目标也面向自己、播放 emoji
            var targetAi = target.GetComponent<HumanEntity>();
            var animalAi = target.GetComponent<AnimalEntity>();
            var targetCtrl = targetAi != null ? targetAi.GetAIController() : (animalAi != null ? animalAi.GetAIController() : null);

            if (animalAi != null)
            {
                // 与动物互动：爱心 ↔ 兴奋 轮流显示
                chatEmojiTimer += Time.deltaTime;
                if (chatEmojiTimer >= 1.2f)
                {
                    chatEmojiTimer = 0f;
                    chatEmojiTurn = !chatEmojiTurn;
                    if (chatEmojiTurn)
                    {
                        ShowEmoji(EmojiType.EmojiHeart);
                        if (targetCtrl != null) targetCtrl.HideEmoji();
                    }
                    else
                    {
                        HideEmoji();
                        if (targetCtrl != null) targetCtrl.ShowEmoji(EmojiType.EmojiExcited);
                    }
                }
            }
            else
            {
                // 与 NPC 闲聊：双方轮流出现气泡
                chatEmojiTimer += Time.deltaTime;
                if (chatEmojiTimer >= 1.2f)
                {
                    chatEmojiTimer = 0f;
                    chatEmojiTurn = !chatEmojiTurn;
                    if (chatEmojiTurn)
                    {
                        ShowEmoji(EmojiType.EmojiBubble);
                        if (targetCtrl != null) targetCtrl.HideEmoji();
                    }
                    else
                    {
                        HideEmoji();
                        if (targetCtrl != null) targetCtrl.ShowEmoji(EmojiType.EmojiBubble);
                    }
                }
            }

            if (targetCtrl != null)
            {
                targetCtrl.FaceDirection(-dir);
                targetCtrl.ChatAsPartner(this);
            }

            // 闲聊：随机播放 emoji-yes 或 emoji-no 动画
            if (string.IsNullOrEmpty(chatAnim))
                chatAnim = UnityEngine.Random.Range(0, 2) == 0 ? "Emoji-Yes" : "Emoji-No";
            animRequest = chatAnim;
        }

        // 作为闲聊对象时记录的发起者（被打断时反向通知对方也结束）
        private AIComponent chatInitiator;

        // 防递归：EndChat 双向通知时避免无限循环
        private bool endingChat;

         //结束闲聊：清除动画意图、恢复 agent、双向通知对端，回归日常 idle。
        public void EndChat()
        {
            if (endingChat) return;
            endingChat = true;
            // 自己是发起者：通知对端（被闲聊对象）也结束
            if (ChatTarget != null)
            {
                var partnerCtrl = ResolvePartnerController(ChatTarget);
                if (partnerCtrl != null) partnerCtrl.EndChat();
                ChatTarget = null;
            }
            // 自己是被闲聊对象：通知发起者也结束
            if (chatInitiator != null)
            {
                chatInitiator.EndChat();
                chatInitiator = null;
            }
            partnerAnim = null;
            isChatting = false;
            animRequest = "Idle";
            chatEmojiTimer = 0f;
            HideEmoji();
            if (Agent != null && Agent.isOnNavMesh) Agent.isStopped = false;
            endingChat = false;
        }

        // 销毁时通知闲聊对端清理引用，避免对端行为树访问已销毁对象（Restart 场景）
        private void OnDestroy()
        {
            if (chatInitiator != null) chatInitiator.EndChat();
            if (ChatTarget != null) EndChat();
        }

         //
        /// 作为闲聊对象被调用：面向对方、随机播 emoji。
        /// 由发起闲聊的一方在靠近后每帧调用。
        /// 
        public void ChatAsPartner(AIComponent initiator)
        {
            if (initiator == null || go == null) return;
            chatInitiator = initiator;
            isChatting = true;
            // 面向发起者
            var dir = initiator.go.transform.position - go.transform.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                var fwd = Quaternion.LookRotation(dir);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, fwd, Time.deltaTime * 8f);
            }
            // 动物：只停下并面向玩家（无 emoji 动画）；人类：随机播 emoji
            if (go.GetComponent<AnimalEntity>() != null)
            {
                animRequest = "Idle";
                return;
            }
            // 随机播 emoji
            if (string.IsNullOrEmpty(partnerAnim))
                partnerAnim = UnityEngine.Random.Range(0, 2) == 0 ? "Emoji-Yes" : "Emoji-No";
            RequestAnim(partnerAnim);
        }

        private string partnerAnim;

        private bool isChatting;

        private string chatAnim;

        public enum EmojiType
        {
            EmojiBubble,
            EmojiHeart,
            EmojiExcited,
        }

        private Billboard emojiBillboard;
        private static Sprite[] emojiSprites;
        private static bool emojiLoaded;

        // 轮流表情计时
        private float chatEmojiTimer;
        private bool chatEmojiTurn;

        private void EnsureEmojiBillboard()
        {
            if (emojiBillboard != null) return;
            if (!emojiLoaded)
            {
                emojiLoaded = true;
                emojiSprites = Resources.LoadAll<Sprite>("Texure/emoji");
            }
            if (emojiSprites == null || emojiSprites.Length == 0) return;

            var obj = new GameObject("EmojiBillboard");
            obj.transform.SetParent(go.transform, false);

            var modelRenderers = go.GetComponentsInChildren<Renderer>();
            float bubbleH = 1.5f;
            foreach (var renderer in modelRenderers)
            {
                obj.transform.position = obj.transform.position.y > renderer.bounds.max.y ? obj.transform.position : renderer.bounds.max;
            }
            var sr = obj.AddComponent<SpriteRenderer>();
            var bb = obj.AddComponent<Billboard>();
            bb.frames = emojiSprites;
            bb.heightScale = bubbleH;
            bb.ApplySprite();
            emojiBillboard = bb;
            obj.SetActive(false);
        }

        public void ShowEmoji(EmojiType type)
        {
            EnsureEmojiBillboard();
            if (emojiBillboard == null) return;
            emojiBillboard.SetFrame((int)type);
            emojiBillboard.gameObject.SetActive(true);
        }

        public void HideEmoji()
        {
            if (emojiBillboard != null) emojiBillboard.gameObject.SetActive(false);
        }

        // 当前正在播放的动画状态（用于避免每帧重复 CrossFade 导致动画卡在过渡）
        private string currentAnim;

        private void UpdateAnim()
        {
            if (Animator == null) return;
            // 优先使用行为树请求的动画
            string want;
            if (!string.IsNullOrEmpty(animRequest))
            {
                want = animRequest;
            }
            else
            {
                // 退化：依据实际速度判断移动/静止（不依赖 hasPath，避免路径计算间隙误判）
                bool moving = Agent != null && Agent.velocity.sqrMagnitude > 0.25f;
                want = moving ? moveAnim : idleAnim;
            }
            if (string.IsNullOrEmpty(want)) return;

            // 只有动画状态变化时才切换；否则保持当前动画自然播放（循环/过渡）
            if (want != currentAnim)
            {
                Animator.CrossFade(want, 0.1f);
                currentAnim = want;
            }
        }

         //供导航节点使用，平滑地转向移动方向。
        public void FaceDirection(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.001f) return;
            var target = Quaternion.LookRotation(dir);
            go.transform.rotation = Quaternion.Slerp(go.transform.rotation, target, Time.deltaTime * 8f);
        }
    }
}
