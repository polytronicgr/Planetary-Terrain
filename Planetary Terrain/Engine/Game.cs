﻿using System;
using SharpDX;
using SharpDX.Windows;
using D3D11 = SharpDX.Direct3D11;
using DInput = SharpDX.DirectInput;
using SharpDX.Mathematics.Interop;
using System.Diagnostics;

namespace Planetary_Terrain {
    class Game : IDisposable {
        #region game management
        private RenderForm renderForm;
        private bool resizePending;

        public Renderer renderer;
        
        private Stopwatch gameTimer;
        private Stopwatch frameTimer;
        #endregion
        #region input
        DInput.Keyboard keyboard;
        DInput.Mouse mouse;

        public UI.InputState InputState;
        Vector2 realMousePos;
        bool lockMouse;
        #endregion

        public UI.Frame ControlPanel;
        PlayerShip player;
        PlayerShip ship2;
        Skybox skybox;
        double zoomd = 0;

        public Game() {
            renderForm = new RenderForm("D3D11 Planets");
            renderForm.MouseMove += (object sender, System.Windows.Forms.MouseEventArgs e) => {
                realMousePos = new Vector2(e.Location.X, e.Location.Y);
            };
            renderForm.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            renderForm.AllowUserResizing = true;
            renderForm.ClientSizeChanged += (object sender, EventArgs e) => {
                resizePending = true;
            };

            DInput.DirectInput directInput = new DInput.DirectInput();
            keyboard = new DInput.Keyboard(directInput);
            mouse = new DInput.Mouse(directInput);

            keyboard.Acquire();
            mouse.Acquire();
            
            renderer = new Renderer(this, renderForm);
            
            Shaders.LoadShaders(renderer.Device, renderer.Context);

            Initialize();
        }
        int frameCount = 0;
        double frameTime = 0;
        public void Run() {
            gameTimer = Stopwatch.StartNew();
            frameTimer = new Stopwatch();
            
            RenderLoop.Run(renderForm, () => {
                double deltaTime = frameTimer.ElapsedTicks / (double)Stopwatch.Frequency;
                frameTimer.Restart();

                frameTime += deltaTime;
                frameCount++;
                if (frameTime > 1) {
                    frameTime = 0;
                    Debug.FPS = frameCount;
                    frameCount = 0;
                }

                Update(deltaTime);
                Draw();
            });
        }
        
        void Initialize() {
            skybox = new Skybox("Data/Textures/colorcube.dds", renderer.Device);
            StarSystem.ActiveSystem = new StarSystem(renderer.Device);
            
            renderer.Camera = new Camera(MathUtil.DegreesToRadians(70), renderForm.ClientSize.Width / (float)renderForm.ClientSize.Width);
            player = new PlayerShip(renderer.Device, renderer.Camera);
            ship2 = new PlayerShip(renderer.Device, renderer.Camera);

            float h = 35;
            RawRectangleF bounds = new RawRectangleF(0, 300, 235, 300 + h);
            ControlPanel = new UI.Frame(null, "Panel1", bounds, renderer.CreateBrush(new Color(.5f, .5f, .5f, .5f)));
            ControlPanel.Draggable = true;

            new UI.TextLabel(ControlPanel, "Title", new RawRectangleF(0, 0, 235, h), "NAVIGATOR", renderer.SegoeUI24, renderer.Brushes["White"]);
            float y = h;
            foreach (Body p in StarSystem.ActiveSystem.bodies) {
                Vector3d d = Vector3d.Normalize(new Vector3d(0, 0.9, -1));
                new UI.TextButton(ControlPanel, p.Label + "Button", new RawRectangleF(5, y, 170, y + h-2), p.Label, renderer.SegoeUI24, renderer.Brushes["Black"], renderer.Brushes["LightGray"],
                    ()=> {
                        player.Position = p.Position + d * (p.SOI + 1000);
                    });
                new UI.TextButton(ControlPanel, p.Label + "SfcButton", new RawRectangleF(175, y, 230, y + h - 2), "Surface", renderer.SegoeUI14, renderer.Brushes["Black"], renderer.Brushes["LightGray"],
                    () => {
                        double hh = p.GetHeight(d);
                        if (p is Planet) {
                            Planet planet = p as Planet;
                            if (planet.HasOcean) {
                                hh = Math.Max(hh, planet.Radius + planet.TerrainHeight * planet.OceanScaleHeight);
                            }
                        }
                        player.Position = p.Position + d * (hh + 50);
                    });
                y += h;
                bounds.Bottom += h;
            }

            y += 10;
            new UI.TextButton(ControlPanel, "NewShipButton", new RawRectangleF(5, y, 170, y + h - 2), "Paste Ship", renderer.SegoeUI24, renderer.Brushes["Black"], renderer.Brushes["LightGray"],
                   () => {
                       ship2.Position = player.Position;
                       ship2.Rotation = player.Rotation;
                   });
            bounds.Bottom += h + 10;

            ControlPanel.LocalBounds = bounds;

            (ControlPanel["EarthSfcButton"] as UI.TextButton).Click();

            renderer.Camera.Ship = player;
            renderer.Camera.Mode = Camera.CameraMode.Ship;
        }

        double t = 0;
        void Update(double deltaTime) {
            t += deltaTime;
            Debug.Track(t.ToString("F2") + "/" + gameTimer.Elapsed.TotalSeconds.ToString("F2") + "   (" + deltaTime.ToString("F4") + ")", "time");

            #region input state update
            InputState.ks = keyboard.GetCurrentState();
            InputState.ms = mouse.GetCurrentState();
            if (InputState.lastks == null) InputState.lastks = InputState.ks;
            if (InputState.lastms == null) InputState.lastms = InputState.ms;
            InputState.mousePos = realMousePos;
            #endregion

            #region ship/camera control
            Vector3d r = Vector3.Zero;
            if (InputState.ks.IsPressed(DInput.Key.W))
                r.X -= -1;
            else if (InputState.ks.IsPressed(DInput.Key.S))
                r.X += -1;
            if (InputState.ks.IsPressed(DInput.Key.A))
                r.Y -= 1;
            else if (InputState.ks.IsPressed(DInput.Key.D))
                r.Y += 1;
            if (InputState.ks.IsPressed(DInput.Key.Q))
                r.Z += 1;
            if (InputState.ks.IsPressed(DInput.Key.E))
                r.Z -= 1;

            r *= .01;
            player.AngularVelocity = Vector3.Lerp(player.AngularVelocity, r, (float)deltaTime);

            if (InputState.ks.IsPressed(DInput.Key.LeftShift))
                player.Throttle += deltaTime * .25;
            else if (InputState.ks.IsPressed(DInput.Key.LeftControl))
                player.Throttle -= deltaTime * .25;
            player.Throttle = MathTools.Clamp01(player.Throttle);

            // Camera view switch
            if (InputState.ks.IsPressed(DInput.Key.V) && !InputState.lastks.IsPressed(DInput.Key.V)) {
                if (renderer.Camera.Mode == Camera.CameraMode.Body) {
                    renderer.Camera.Mode = Camera.CameraMode.Ship;
                    renderer.Camera.Ship = player;
                } else
                    renderer.Camera.Mode = Camera.CameraMode.Body;
            }

            // Mouse lock
            if (InputState.ks.IsPressed(DInput.Key.F1) && !InputState.lastks.IsPressed(DInput.Key.F1)) {
                lockMouse = !lockMouse;
            
                if (lockMouse)
                    System.Windows.Forms.Cursor.Hide();
                else
                    System.Windows.Forms.Cursor.Show();
            }
            
            // Mouse look
            if (lockMouse || InputState.ms.Buttons[1] || InputState.ms.Buttons[2]) {
                Vector2 delta;
                if (lockMouse) {
                    delta = new Vector2(InputState.ms.X, InputState.ms.Y);
                    System.Windows.Forms.Cursor.Position = new System.Drawing.Point(renderForm.ClientRectangle.X + renderForm.ClientSize.Width / 2, renderForm.ClientRectangle.Y + renderForm.ClientSize.Height / 2);
                } else
                    delta = InputState.mousePos - InputState.lastMousePos;

                if (InputState.ms.Buttons[2])
                    renderer.Camera.PostRotation += new Vector3(delta.Y, delta.X, 0) * .003f;
                else
                    renderer.Camera.Rotation += new Vector3(delta.Y, delta.X, 0) * .003f;
            }
            // zoom
            zoomd = -InputState.ms.Z / 120;
            renderer.Camera.Zoom += zoomd * deltaTime;
            zoomd *= .8;
            #endregion

            if (InputState.ks.IsPressed(DInput.Key.F2) && !InputState.lastks.IsPressed(DInput.Key.F2))
                renderer.DrawGUI = !renderer.DrawGUI;

            if (InputState.ks.IsPressed(DInput.Key.Tab) && !InputState.lastks.IsPressed(DInput.Key.Tab))
                renderer.Camera.BodyIndex = (renderer.Camera.BodyIndex + 1) % StarSystem.ActiveSystem.bodies.Count;

            if (InputState.ks.IsPressed(DInput.Key.F) && !InputState.lastks.IsPressed(DInput.Key.F))
                renderer.DrawWireframe = !renderer.DrawWireframe;

            player.Update(deltaTime);
            Profiler.Begin("StarSystem Update");
            StarSystem.ActiveSystem.Update(renderer, renderer.Device, deltaTime);
            Profiler.End();
            Profiler.Begin("QuadNode Update");
            QuadNode.Update();
            Profiler.End();
            renderer.Camera.Update();
            
            ControlPanel.Update((float)deltaTime, InputState);

            #region input state update
            InputState.lastks = InputState.ks;
            InputState.lastms = InputState.ms;
            InputState.lastMousePos = InputState.mousePos;
            #endregion
        }
        void Draw() {
            if (resizePending)
                renderer.Resize(renderForm.ClientSize.Width, renderForm.ClientSize.Height);

            Debug.BeginFrame();

            renderer.BeginDrawFrame();
            renderer.TotalTime = gameTimer.Elapsed.TotalSeconds;
            renderer.Clear(Color.Black);
            
            // 3d
            Profiler.Begin("3d Draw");
            skybox.Draw(renderer);

            StarSystem.ActiveSystem.Draw(renderer, player.LinearVelocity.Length());
            
            player.Draw(renderer);
            ship2.Draw(renderer);
            Profiler.End();

            // 2d
            Profiler.Begin("2d Draw");
            if (renderer.DrawGUI) {
                renderer.D2DContext.BeginDraw();
                ControlPanel.Draw(renderer);
                renderer.D2DContext.EndDraw();
            }
            Profiler.End();

            Debug.EndFrame();

            if (renderer.DrawGUI)
                Debug.Draw(renderer, player);
            
            renderer.EndDrawFrame();
        }

        public void Dispose() {
            // scene stuff
            StarSystem.ActiveSystem.Dispose();
            skybox.Dispose();
            player?.Dispose();
            ship2?.Dispose();
            ControlPanel.Dispose();

            Shaders.Dispose();
            
            // other stuff
            keyboard.Dispose();
            mouse.Dispose();

            renderer.Dispose();
            renderForm.Dispose();
        }
    }
}