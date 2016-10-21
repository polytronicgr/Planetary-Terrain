﻿using System;
using SharpDX;
using System.Collections.Generic;

namespace Planetary_Terrain {
    struct Force {
        public Vector3d ForceVector;
        public Vector3d Offset;

        public Force(Vector3d force, Vector3d offset) {
            ForceVector = force;
            Offset = offset;
        }
    }
    struct Orbit {
        public double Eccentricity;
        public double SemimajorAxis;
        public double Inclination;
        public double Longitude;
        public double ArgumentPeriapsis;
        public double ArgumentAnomaly;
        public PhysicsBody Orbitee;

        // TODO: finish orbits
        public static Orbit FromCartesian(Vector3d position, Vector3d velocity, double mass) {
            return new Orbit() {

            };
        }
        public void ToCartesian(out Vector3d position, out Vector3d velocity) {
            position = Vector3.Zero;
            velocity = Vector3.Zero;
        }
    }
    abstract class PhysicsBody {
        public Vector3d Position;
        public Vector3d Velocity;
        public Matrix Rotation = Matrix.Identity;
        public Vector3d AngularVelocity;
        public double Mass;
        public double Drag;
        public Orbit Orbit;

        public bool DisablePhysics = false;

        public List<Force> Forces = new List<Force>();
        
        public PhysicsBody(double mass) {
            Mass = mass;
            Drag = 1;
        }

        public void AddForce(Vector3d force, Vector3d pos) {
            Forces.Add(new Force(force, pos));
        }

        public virtual void Update(double deltaTime) {
            if (DisablePhysics) return;

            CelestialBody b = StarSystem.ActiveSystem.GetNearestBody(Position);
            double h = b != null ? (b.Position - Position).Length() : 0;
            if (b != this) {
                if (b != null) {
                    // perform drag
                    if (b is Planet) {
                        Atmosphere a = (b as Planet).Atmosphere;
                        if (a != null && h < a.Radius) {
                            double v = Velocity.LengthSquared();
                            if (v > .1) {
                                double temp;
                                double pressure;
                                double density;
                                double c;
                                a.MeasureProperties(h, out pressure, out density, out temp, out c);

                                double drag = .5 * density * v * Drag;
                                AddForce(-Vector3d.Normalize(Velocity) * drag, Vector3.Zero);
                            }
                        }
                    }
                }
            }

            foreach (Force f in Forces) {
                Velocity += (f.ForceVector / Mass) * deltaTime;

                // TODO: calculate and apply torque
            }

            Position += Velocity * deltaTime;
            Rotation *= Matrix.RotationAxis(Rotation.Right, (float)AngularVelocity.X) * Matrix.RotationAxis(Rotation.Up, (float)AngularVelocity.Y) * Matrix.RotationAxis(Rotation.Backward, (float)AngularVelocity.Z);

            Forces.Clear();

            if (b != this) {
                // check collision
                if (b != null) {
                    Vector3d p = Position - b.Position;
                    p /= h;
                    double t = b.GetHeight(p);
                    if (h < t) {
                        Position = b.Position + p * t;

                        Vector3d n = b.GetNormal(p);
                        // vector rejection
                        Velocity -= n * Vector3d.Dot(Velocity, n);

                        // friction
                        double grav = Physics.G * b.Mass / (h * h);
                        AddForce(Vector3d.Normalize(-Velocity) * .2 * Mass * grav, Vector3.Zero);
                    }
                }
            }
        }
        public virtual void PostUpdate() { }

        public virtual void Draw(Renderer renderer) { }
    }
    class Physics {
        public const double LIGHT_SPEED = 299792458;
        public const double LIGHT_YEAR = 9.4605284e15;
        public const double G = 6.674e-11;

        public static string FormatSpeed(double meters) {
            if (meters < Physics.LIGHT_SPEED) {
                string[] u = { "m/s", "km/s", "Mm/s", "Gm/s" };
                int i = 0;
                while (meters / 1000 > 1 && i < u.Length - 1) {
                    i++;
                    meters /= 1000;
                }
                return meters.ToString("F1") + u[i];
            } else {
                return (meters / Physics.LIGHT_SPEED).ToString("F4") + "c";
            }
        }
        public static string FormatDistance(double meters) {
            if (meters < Physics.LIGHT_YEAR) {
                string[] u = { "m", "km", "Mm", "Gm" };
                int i = 0;
                while (meters / 1000 > 1 && i < u.Length - 1) {
                    i++;
                    meters /= 1000;
                }
                return meters.ToString("F1") + u[i];
            } else {
                return (meters / Physics.LIGHT_YEAR).ToString("F4") + "ly";
            }
        }
        public static string CalculateTime(double dist, double speed) {
            ulong totalSeconds = (ulong)(dist / speed);
            ulong totalMinutes = totalSeconds / 60L;
            ulong totalHours = totalMinutes / 60L;
            ulong totalDays = totalHours / 24L;
            ulong totalYears = totalDays / 356L;

            ulong seconds = totalSeconds % 60L;
            ulong minutes = totalMinutes % 60L;
            ulong hours = totalHours % 60L;
            ulong days = totalDays % 24L;
            ulong years = totalYears % 365L;
            
            if (totalSeconds < 60L)
                return string.Format("{0} seconds", totalSeconds);
            else if (totalMinutes < 60L)
                return "\nArrive in " + string.Format("{0}m:{1}s", minutes, seconds);
            else if (totalHours < 24L)
                return "\nArrive in " + string.Format("{0}h:{1}m", hours, minutes);
            else if (totalDays < 365L)
                return "\nArrive in " + string.Format("{0}d:{1}h", days, hours);
            else
                return "\nArrive in " + string.Format("{0}y:{0}d", years, days);
        }

        public List<PhysicsBody> bodies = new List<PhysicsBody>();

        /// <summary>
        /// Calculates gravity force vector
        /// </summary>
        /// <returns>Force vector (from a to b)</returns>
        public static Vector3d Gravity(double deltaTime, PhysicsBody a, PhysicsBody b) {
            Vector3d dir = b.Position - a.Position;
            double l2 = dir.LengthSquared();
            if (l2 > .1) {
                double magnitude = G * (a.Mass * b.Mass) / l2;
                dir.Normalize();

                return dir * magnitude;
            }
            return Vector3.Zero;
        }

        public void AddBody(PhysicsBody body) {
            if (!bodies.Contains(body))
                bodies.Add(body);
        }
        public void RemoveBody(PhysicsBody body) {
            bodies.Remove(body);
        }

        public void Update(double deltaTime) {
            foreach (PhysicsBody b in bodies)
                if (!b.DisablePhysics)
                    foreach (PhysicsBody cb in StarSystem.ActiveSystem.bodies)
                        if (b != cb)
                            b.AddForce(Gravity(deltaTime, b, cb), Vector3.Zero);

            foreach (PhysicsBody b in bodies)
                b.Update(deltaTime);

            foreach (PhysicsBody b in bodies)
                b.PostUpdate();
        }

        public void Draw(Renderer renderer) {
            foreach (PhysicsBody b in bodies)
                b.Draw(renderer);
        }
    }
}