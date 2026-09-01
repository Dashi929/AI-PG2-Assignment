using EcsFramework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace CameraTools
{
    public class RtsCamera : MonoBehaviour
    {
        public float pitch = 50f;
        public float yaw = 0f;
        public float distance = 40f;
        public float minDistance = 12f;
        public float maxDistance = 120f;

        public float edgeScrollSpeed = 25f;
        public float keyboardSpeed = 25f;
        public float edgeSize = 16f;
        public bool edgeScrollEnabled = true;

        public float zoomSpeed = 8f;
        public float smoothTime = 0.15f;

        // 旋转：右键按住拖动（左右=偏航、上下=俯仰），灵敏度（像素 → 度）
        public float rightDragYawSensitivity = 0.2f;
        public float rightDragPitchSensitivity = 0.15f;
        public float pitchMin = 15f;
        public float pitchMax = 80f;

        // 地面约束：相机不穿过地面（低于该高度会被抬升）
        public float minCameraHeight = 3f;

        // 选中光圈：半径、颜色（半透明避免遮挡模型）、贴地偏移
        public float selectionRingRadius = 1.1f;
        public Color selectionRingColor = new Color(0.3f, 1f, 0.3f, 0.4f);
        public float selectionRingYOffset = 0.05f;

        // 右键命令标记：半径、颜色（半透明；命令完成/取消后自动隐藏）
        public float commandMarkRadius = 1.2f;
        public Color commandMarkColor = new Color(1f, 0.25f, 0.2f, 0.4f);

        // 视角注视的目标点（世界坐标）
        private Vector3 focusPoint;
        private Vector3 focusVelocity;

        // 初始镜头状态（用于 R 键重置）
        private float startPitch, startYaw, startDistance;
        private Vector3 startFocus;

        // 鼠标中键拖动平移
        private Vector2 lastMousePos;

        // 右键拖动旋转：按下位置 / 按下状态 / 是否已识别为拖动
        private Vector2 rightDownPos;
        private bool rightButtonDown;
        private bool rightDragging;

        // 当前选中的 NPC
        private AIComponent selectedNpc;

        // 取消选中后延迟结束闲聊：3 秒计时
        private AIComponent pendingChatNpc;
        private float cancelChatTimer;

        // 选中光圈（圆环）
        private LineRenderer selectionRing;

        // 右键命令标记（圆环 + 存在时间）
        private LineRenderer commandMark;
        private Transform commandMarkFollowTarget;   // 跟随模式下红圈跟随的目标（生物）
        private AIComponent commandNpc;              // 发起当前命令的 NPC（命令完成/取消后隐藏红圈）

        private void Start()
        {
            // 初始焦点 = 地图中心（有地形生成器则用其尺寸中心）
            var terrainGen = FindFirstObjectByType<ProceduralTerrain.TerrainGenerator>();
            if (terrainGen != null && terrainGen.TerrainData != null)
            {
                var size = terrainGen.TerrainData.size;
                float cx = terrainGen.transform.position.x + size.x * 0.5f;
                float cz = terrainGen.transform.position.z + size.z * 0.5f;
                float cy = terrainGen.transform.position.y + size.y * 0.5f;
                focusPoint = new Vector3(cx, cy, cz);
            }
            else
            {
                // 兜底：相机正下方的地面位置
                RaycastHit hit;
                if (Physics.Raycast(transform.position, Vector3.down, out hit, 500f))
                    focusPoint = hit.point;
                else
                    focusPoint = transform.position + transform.forward * distance;
            }
            lastMousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

            // 记录初始镜头状态
            startPitch = pitch;
            startYaw = yaw;
            startDistance = distance;
            startFocus = focusPoint;

            CreateSelectionRing();
            CreateCommandMark();
        }


        private void Update()
        {
            if (Keyboard.current == null || Mouse.current == null) return;

            // 鼠标不在游戏窗口内时不操作镜头（避免切到其他程序时镜头乱动）
            bool mouseInWindow = IsMouseInWindow();
            // 鼠标在 UI 上（参数面板/按钮等）时禁止所有镜头操作，避免误拖镜头/误发命令
            bool overUI = UnityEngine.EventSystems.EventSystem.current != null
                          && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (mouseInWindow && !overUI)
            {
                HandleZoom(Mouse.current);
                HandlePan(Keyboard.current, Mouse.current);
                HandleRotate(Keyboard.current, Mouse.current);
                HandleSelection(Mouse.current);
            }
            UpdateSelectionRing();
            UpdateCommandMark();
            UpdateCancelChat();

            // 相机绕 focusPoint 旋转并保持距离
            float focusY = Mathf.SmoothDamp(focusPoint.y, ClampFocusY(focusPoint.y), ref focusVelocity.y, smoothTime);
            focusPoint = new Vector3(focusPoint.x, focusY, focusPoint.z);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desired = focusPoint - rot * Vector3.forward * distance;

            // 相机不穿过地面：低于最小高度则抬升（保持注视方向）
            float groundY = SampleGroundHeight(desired.x, desired.z);
            if (desired.y < groundY + minCameraHeight)
                desired.y = groundY + minCameraHeight;

            transform.position = desired;
            transform.rotation = rot;
        }

         //右键按住拖动旋转镜头：左右拖动转偏航角、上下拖动调俯仰角；R 重置镜头。
        private void HandleRotate(Keyboard kb, Mouse mouse)
        {
            if (kb.rKey.wasPressedThisFrame)
            {
                ResetCamera();
                return;
            }

            // 右键按住拖动：左右=偏航、上下=俯仰（拖动超过阈值视为拖动，不触发右键命令）
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightButtonDown = true;
                rightDragging = false;
                rightDownPos = mouse.position.ReadValue();
            }
            else if (rightButtonDown && mouse.rightButton.isPressed)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector2 delta = mp - rightDownPos;
                if (!rightDragging && delta.magnitude > 10f) rightDragging = true;
                if (rightDragging)
                {
                    yaw += delta.x * rightDragYawSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * rightDragPitchSensitivity, pitchMin, pitchMax);
                    rightDownPos = mp;   // 增量式，避免累积
                }
            }
        }

         //重置镜头：回到初始视角（俯仰/偏航/距离）与地图中心。
        private void ResetCamera()
        {
            pitch = startPitch;
            yaw = startYaw;
            distance = startDistance;
            focusPoint = startFocus;
            focusVelocity = Vector3.zero;
        }

         //采样世界位置 (x, z) 的地面高度（含地形偏移）。
        private float SampleGroundHeight(float x, float z)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(x, 500f, z), Vector3.down, out hit, 1000f))
                return hit.point.y;
            return 0f;
        }

         //判断鼠标指针是否位于游戏窗口内。
        private bool IsMouseInWindow()
        {
            if (Mouse.current == null) return false;
            var pos = Mouse.current.position.ReadValue();
            return pos.x >= 0f && pos.x <= Screen.width &&
                   pos.y >= 0f && pos.y <= Screen.height;
        }

         //滚轮缩放：改变相机到目标的距离。
        private void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

         //平移：边缘滚动 + 鼠标中键拖动 + WASD。
        private void HandlePan(Keyboard kb, Mouse mouse)
        {
            Vector3 move = Vector3.zero;

            // 屏幕边缘滚动
            if (edgeScrollEnabled)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mp.x <= edgeSize) move -= Vector3.right;
                else if (mp.x >= Screen.width - edgeSize) move += Vector3.right;
                if (mp.y <= edgeSize) move -= Vector3.forward;
                else if (mp.y >= Screen.height - edgeSize) move += Vector3.forward;
            }

            // 键盘（WASD/方向键平移；R/F 由 HandleRotate 处理重置与俯仰）
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += Vector3.forward;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= Vector3.forward;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= Vector3.right;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += Vector3.right;

            float panSpeed = keyboardSpeed;
            if (move.sqrMagnitude > 0.001f)
                panSpeed = Mathf.Max(panSpeed, edgeScrollSpeed);
            move = move.normalized * panSpeed * Time.deltaTime;

            // 鼠标中键拖动
            if (mouse.middleButton.isPressed)
            {
                Vector2 mp = mouse.position.ReadValue();
                Vector2 delta = mp - lastMousePos;
                float scale = distance * 0.002f;
                move.x -= delta.x * scale;
                move.z -= delta.y * scale;
            }

            // 平移要与相机的偏航方向对齐
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            focusPoint += (fwd * move.z + right * move.x) + Vector3.up * move.y;

            lastMousePos = mouse.position.ReadValue();
        }

         //左键选中 NPC / 空白处取消；右键命令选中 NPC。
        private void HandleSelection(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var prev = selectedNpc;
                selectedNpc = PickNpcAtScreen(mouse.position.ReadValue());
                // 取消选中（点到空白或切换目标）：原 NPC 立即下车（若在乘车）
                if (selectedNpc != prev && prev != null)
                {
                    prev.ExitVehicle();
                    // 若在闲聊，3 秒后结束
                    if (prev.ChatTarget != null)
                    {
                        pendingChatNpc = prev;
                        cancelChatTimer = 3f;
                    }
                    else
                    {
                        cancelChatTimer = 0f;
                        pendingChatNpc = null;
                    }
                }
                else
                {
                    cancelChatTimer = 0f;
                    pendingChatNpc = null;
                }
            }
            else if (mouse.rightButton.wasReleasedThisFrame)
            {
                // 右键单击（未拖动）：命令选中 NPC；右键拖动旋转由 HandleRotate 处理
                if (rightButtonDown && !rightDragging && selectedNpc != null)
                {
                    CommandSelected(mouse.position.ReadValue());
                    // 发出新指令：立即结束原闲聊
                    if (pendingChatNpc != null) pendingChatNpc.EndChat();
                    pendingChatNpc = null;
                    cancelChatTimer = 0f;
                }
                rightButtonDown = false;
                rightDragging = false;
            }
        }

         //推进取消选中的闲聊结束计时：3 秒后结束原闲聊 NPC。
        private void UpdateCancelChat()
        {
            if (pendingChatNpc == null || cancelChatTimer <= 0f) return;
            cancelChatTimer -= Time.deltaTime;
            if (cancelChatTimer <= 0f)
            {
                pendingChatNpc.EndChat();
                pendingChatNpc.CancelCommand();
                pendingChatNpc = null;
            }
        }

         //从屏幕点拾取一个 NPC（只认 HumanEntity 上的胶囊碰撞体）。
        private static AIComponent PickNpcAtScreen(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 1000f);
            for (int i = 0; i < hits.Length; i++)
            {
                var he = hits[i].collider.GetComponentInParent<HumanEntity>();
                if (he != null)
                {
                    var ai = he.GetAIController();
                    if (ai != null) return ai;
                }
            }
            return null;
        }

         //命令选中的 NPC：右键点 NPC 则上前闲聊；点车则上车；点地面则移动到该点。
        private void CommandSelected(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            var hits = Physics.RaycastAll(ray, 1000f);

            // 优先处理 NPC / 车 / 动物（可能同时命中地形，需要 RaycastAll 找到最近的可交互物体）
            HumanEntity npc = null;
            CarEntity car = null;
            AnimalEntity animal = null;
            RaycastHit groundHit = default;
            bool hasGround = false;
            float bestNpc = float.MaxValue, bestCar = float.MaxValue, bestAnimal = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                var he = h.collider.GetComponentInParent<HumanEntity>();
                if (he != null)
                {
                    float d = h.distance;
                    if (d < bestNpc) { bestNpc = d; npc = he; }
                }
                var ce = h.collider.GetComponentInParent<CarEntity>();
                if (ce != null)
                {
                    float d = h.distance;
                    if (d < bestCar) { bestCar = d; car = ce; }
                }
                var ae = h.collider.GetComponentInParent<AnimalEntity>();
                if (ae != null)
                {
                    float d = h.distance;
                    if (d < bestAnimal) { bestAnimal = d; animal = ae; }
                }
                // 记录最近的地面命中（非 NPC/车/动物）
                if (!hasGround || h.distance < groundHit.distance)
                {
                    groundHit = h;
                    hasGround = true;
                }
            }

            // 优先 NPC，其次动物，其次车，最后地面
            if (npc != null && bestNpc < bestAnimal && bestNpc < bestCar)
            {
                selectedNpc.CommandChat(npc.transform);
                ShowCommandMark(GroundPoint(npc.transform.position), npc.transform, selectedNpc);
                return;
            }
            if (animal != null && bestAnimal < bestCar)
            {
                // 右键点动物：选中的 NPC 走过去与动物互动（停下、面向、播动画）
                selectedNpc.CommandChat(animal.transform);
                ShowCommandMark(GroundPoint(animal.transform.position), animal.transform, selectedNpc);
                return;
            }
            if (car != null)
            {
                var carComp = ResolveCarComponent(car);
                if (carComp != null)
                {
                    selectedNpc.CommandRide(carComp);
                    ShowCommandMark(GroundPoint(car.transform.position), null, selectedNpc);
                    return;
                }
            }
            if (!hasGround) return;

            // 地面：移动到该点（排除湖泊等不可行走区域）
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(groundHit.point, out navHit, 10f, NavMesh.AllAreas & ~(1 << 1)))
            {
                selectedNpc.CommandMoveTo(navHit.position);
                ShowCommandMark(navHit.position, null, selectedNpc);
            }
        }

         //把世界点向下投影到地面，用于让命令标记/光圈贴地显示。
        private Vector3 GroundPoint(Vector3 worldPos)
        {
            RaycastHit hit;
            if (Physics.Raycast(worldPos + Vector3.up * 0.5f, Vector3.down, out hit, 100f))
                return hit.point;
            return worldPos;
        }

         //从 CarEntity 解析其 ECS CarComponent（若无则 null）。
        private static CarComponent ResolveCarComponent(CarEntity carEntity)
        {
            if (carEntity == null) return null;
            var entity = carEntity.Entity;
            if (entity == null || !entity.Has<CarComponent>()) return null;
            return entity.Get<CarComponent>();
        }

         //创建选中光圈：一个贴地的圆环（无视深度测试，始终显示在最上层）。
        private void CreateSelectionRing()
        {
            var ringGo = new GameObject("SelectionRing");
            ringGo.transform.SetParent(transform, false);
            selectionRing = ringGo.AddComponent<LineRenderer>();

            selectionRing.useWorldSpace = true;
            selectionRing.loop = true;
            selectionRing.positionCount = 48;
            selectionRing.startWidth = 0.12f;
            selectionRing.endWidth = 0.12f;
            selectionRing.material = LoadMarkerMaterial();
            selectionRing.startColor = selectionRingColor;
            selectionRing.endColor = selectionRingColor;
            selectionRing.enabled = false;
        }

         //
        /// 加载标记材质（Resources 资产）。用资产而非运行时 new Material，
        /// 确保构建时 SelectionMarkerURP 的变体被包含，避免 WebGL 变紫。
        /// 
        private static Material LoadMarkerMaterial()
        {
            var asset = Resources.Load<Material>("Materials/SelectionMarkerURP");
            if (asset != null) return asset;
            var shader = Shader.Find("ProceduralTerrain/SelectionMarkerURP");
            return shader != null ? new Material(shader) : null;
        }

         //让光圈跟随选中 NPC 的脚下；无选中则隐藏。
        private void UpdateSelectionRing()
        {
            if (selectionRing == null) return;
            if (selectedNpc == null || selectedNpc.go == null)
            {
                selectionRing.enabled = false;
                return;
            }

            // 找 NPC 脚下的地面高度
            Vector3 pos = selectedNpc.go.transform.position;
            float groundY = pos.y;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out hit, 50f))
                groundY = hit.point.y;
            groundY += selectionRingYOffset;

            // 生成圆环顶点
            var center = new Vector3(pos.x, groundY, pos.z);
            for (int i = 0; i < selectionRing.positionCount; i++)
            {
                float a = i / (float)selectionRing.positionCount * Mathf.PI * 2f;
                selectionRing.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * selectionRingRadius);
            }
            selectionRing.enabled = true;
        }

         //创建右键命令标记（红色圆环，无视深度测试），默认隐藏。
        private void CreateCommandMark()
        {
            var markGo = new GameObject("CommandMark");
            markGo.transform.SetParent(transform, false);
            commandMark = markGo.AddComponent<LineRenderer>();

            commandMark.useWorldSpace = true;
            commandMark.loop = true;
            commandMark.positionCount = 32;
            commandMark.startWidth = 0.15f;
            commandMark.endWidth = 0.15f;
            commandMark.material = LoadMarkerMaterial();
            commandMark.startColor = commandMarkColor;
            commandMark.endColor = commandMarkColor;
            commandMark.enabled = false;
        }

         //
        /// 在指定世界位置显示命令标记（目的地留痕，持续显示直到下次命令或取消）。
        /// 若传入 followTarget，则红圈持续贴在目标脚下并随目标移动。
        /// 
        private void ShowCommandMark(Vector3 pos, Transform followTarget = null, AIComponent npc = null)
        {
            if (commandMark == null) return;
            commandMarkFollowTarget = followTarget;
            commandNpc = npc != null ? npc : selectedNpc;
            DrawCommandMark(pos, 1f);
            commandMark.enabled = true;
        }

         //每帧更新命令标记：跟随模式贴目标脚下；移动模式留在目的地。
        /// 发起命令的 NPC 不再有活动命令（已到达/被取消）时隐藏红圈。
        private void UpdateCommandMark()
        {
            if (commandMark == null) return;

            // 命令完成或取消：隐藏红圈
            if (commandNpc == null ||
                (commandNpc.CommandDestination == null && commandNpc.ChatTarget == null && commandNpc.RideCar == null))
            {
                commandMark.enabled = false;
                return;
            }

            Vector3 drawPos = commandMarkFollowTarget != null
                ? GroundPoint(commandMarkFollowTarget.position)
                : commandMarkPosition;

            DrawCommandMark(drawPos, 1f);
            commandMark.enabled = true;
        }

        // 命令标记的中心位置（世界坐标）
        private Vector3 commandMarkPosition;

         //绘制命令标记圆环；t 从 1 到 0 逐渐淡出缩小。
        private void DrawCommandMark(Vector3 pos, float t)
        {
            commandMarkPosition = pos;
            var color = commandMarkColor;
            color.a = commandMarkColor.a * t;
            commandMark.startColor = color;
            commandMark.endColor = color;
            float r = commandMarkRadius * (0.5f + 0.5f * t);
            for (int i = 0; i < commandMark.positionCount; i++)
            {
                float a = i / (float)commandMark.positionCount * Mathf.PI * 2f;
                commandMark.SetPosition(i, pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
            }
        }

         //限制焦点高度不低于地面（避免钻地）。
        private float ClampFocusY(float y)
        {
            return Mathf.Max(y, 0f);
        }
    }
}
