using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Game.Components;
using VRage.Game.Entity;

namespace _800mm_tracking
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class Torp_Tracking : MySessionComponentBase
    {
        //Missle stat storage
        //If you wish to add missiles to guidance add them here
        readonly Dictionary<string, PhoenixMissileDef> MissileStats = new Dictionary<string, PhoenixMissileDef>()
        {
            /*
            Change the things with *** around them to what it says inside.
            In order to add numbers < 1 you must put an f behind it. I dont know why you just do.
            { "***Missile SubtypeID***" , new PhoenixMissileDef("***Missile SubtypeID***", ***Track Radius*** , ***MissileLifeTime***, ***MissileTurnSpeed***) },
            Track Radius: Will set the sphere that the missile searches inside. Keep in mind that the real range of tracking is the diameter of the sphere and not the radius.
            MissileLifeTime: The ammount of time the missile will be allowed to live without aquireing a target.
            MissileTurnSpeed: This will set how fast the missile is able to turn to face a target. RECOMENDED TO KEEP THIS UNDER 1 AS THIS WILL BE UPDATED 60 TIMES IN A SECOND
            Example of correctly setup missile.
            { "Ace_800mmTorpedoStrike", new PhoenixMissileDef("Ace_800mmTorpedoStrike", 1000, 60, 0.2f) },
            */

            { "Ace_220mmBurstMissile_Guided", new PhoenixMissileDef("Ace_220mmBurstMissile_Guided", 100, 3600, 0.5f) },

        };

        //The missile Class definition
        public class PhoenixMissileDef
        {
            public string Subtype;              //SubtypeID of the missile
            public float MissileTrackRadius;    //Radius of the Sphere that the missile uses to target
            public float MissileLifeTime;       //The life time without a target of the missile
            public float MissileTurnSpeed;      //The speed in degrees of how the missile turns

            //Constructor
            public PhoenixMissileDef(string subtype, float trackRadius, float lifeTime, float missileTurnSpeed)
            {
                Subtype = subtype;
                MissileTrackRadius = trackRadius;
                MissileLifeTime = lifeTime;
                MissileTurnSpeed = missileTurnSpeed;
            }
        }

        /*
        Main missile guidance code
        This is where the main body of the code begins
        */
        private class MissileGuider
        {
            public IMyMissile missile;      //The missile entity
            public IMyEntity target;        //The current target

            PhoenixMissileDef MissileStats; //Holds the stats of the missile for guidance

            private float time = 0;
            private const float ticktime = 1f / 60f;
            int TimeWithoutTarget = 0;

            Vector3 direction = Vector3.Zero;
            Vector3 velocity = Vector3.Zero;
            Vector3 position = Vector3.Zero;
            MyLog.Default.WriteLine("MissileGuider Started");

            public MissileGuider(IMyMissile missile, PhoenixMissileDef MissileStats_)
            {
                this.missile = missile;

                direction = this.missile.WorldMatrix.Forward;

                position = this.missile.GetPosition();

                this.MissileStats = MissileStats_;

                UpdateVelocity();
            }

            /* Main updating of the missile */
            public void Update()
            {
                MyAPIGateway.Utilities.ShowMessage("Missile guide Update Start");

                UpdateTarget();

                UpdateDirectionNoPrediction();

                UpdateVelocity();

                UpdateTransform();

                

                time += ticktime;

                if (target == null)
                {
                    TimeWithoutTarget++;
                }
                else
                {
                    TimeWithoutTarget = 0;
                }

                if (TimeWithoutTarget == MissileStats.MissileLifeTime)
                {
                    MyAPIGateway.Utilities.ShowMessage("Guided missile out of life time: Deleating");
                    missile.MaxTrajectory = 1;
                    missile.DoDamage(1, MyStringHash.NullOrEmpty, true, null, 0, 0, true);
                }

            }

            public void UpdateTarget()
            {
                MyAPIGateway.Utilities.ShowMessage("Updating target");

                /* Adding Bounding Sphere infront of projectile */
                Vector3D posAhead = missile.WorldMatrix.Translation + missile.WorldMatrix.Forward * (MissileStats.MissileTrackRadius); //Thanks to Digi#9441 on keen's discord for this formula.
                BoundingSphereD TargetingSphere = new BoundingSphereD(posAhead, MissileStats.MissileTrackRadius);

                /* Adding all entitys in the radius to list inradius */
                List<MyEntity> inradius = new List<MyEntity>();
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref TargetingSphere, inradius);


                List<IMyEntity> HostileInRadius = new List<IMyEntity>(); //Holds all hostile grids in the sphere

                foreach (var entity in inradius) // finds cubeblocks and if it detects an enemy, adds them to a list of hostiles in range
                {
                    if (entity != null) // anti error
                    {

                        if (entity is MyCubeGrid)  // detects if entity is cubeblock
                        {
                            MyCubeGrid gridNearby = entity as MyCubeGrid; // cast IMyEntity into MyCubeGrid
                            if (gridNearby.IsPowered)
                            {
                                List<long> majorityOwners = new List<long>(gridNearby.BigOwners); // get owners
                                foreach (long id in majorityOwners)
                                {
                                    if (!missile.IsCharacterIdFriendly(id)) // detects if owner is not friendly, if true, puts into HostileInRadius
                                    {
                                        HostileInRadius.Add(gridNearby);
                                    }
                                }
                            }
                        }
                    }
                }

                //Clearing the list of entitys
                inradius.Clear();

                double closestDistSq = double.MaxValue;
                IMyEntity closestEnt = null;

                foreach (var entity in HostileInRadius)
                {
                    double distSq = Vector3D.DistanceSquared(entity.GetPosition(), missile.GetPosition());
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestEnt = entity;
                    }
                }

                if (closestEnt != null)
                {
                    target = closestEnt;
                    HostileInRadius.Clear();
                }

                MyAPIGateway.Utilities.ShowMessage("Update target End");
            } //End public void UpdateTarget()

            public void UpdateVelocity()
            {
                MyMissileAmmoDefinition ammo = (MyMissileAmmoDefinition)missile.AmmoDefinition;
                float speed = Math.Min(ammo.MissileInitialSpeed + ammo.MissileAcceleration * time, ammo.DesiredSpeed);
                velocity = direction * speed;

                if (missile.Physics != null)
                    missile.Physics.LinearVelocity = velocity;
            } //end UpdateVelocity()

            public void UpdateDirectionNoPrediction()
            {
                MyAPIGateway.Utilities.ShowMessage("Update Direction");
                if (target != null && !target.MarkedForClose)
                {
                    Vector3 TargetDirection = target.WorldVolume.Center - missile.GetPosition();
                    TargetDirection.Normalize();

                    Vector3 rotationAxis = Vector3.Cross(direction, TargetDirection);
                    rotationAxis.Normalize();

                    MatrixD rotationMatrix = MatrixD.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(rotationAxis, MathHelper.ToRadians(MissileStats.MissileTurnSpeed)));

                    direction = Vector3.Transform(direction, rotationMatrix);
                    direction.Normalize();
                    target = null;
                }
                MyAPIGateway.Utilities.ShowMessage("Update direction end");
            } //End public void UpdateDirectionNoPrediction()

            public void UpdateTransform()
            {
                MyAPIGateway.Utilities.ShowMessage("update transform");
                if (missile.Physics == null)
                {
                    position = position + velocity * ticktime;
                    missile.SetPosition(position);
                }

                MatrixD worldMatrix = missile.WorldMatrix;
                worldMatrix.Forward = direction;
                missile.WorldMatrix = worldMatrix;
                MyAPIGateway.Utilities.ShowMessage("Update transform end");
            } //End public void UpdateTransform() 
        } //End private class MissileGuider

        //Dictionary containing all active guided missiles
        Dictionary<long, MissileGuider> missileGuiders = new Dictionary<long, MissileGuider>();

        public override void BeforeStart()
        {
            MyAPIGateway.Missiles.OnMissileAdded += OnMissileAdded;
            MyAPIGateway.Missiles.OnMissileRemoved += OnMissileRemoved;
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Missiles.OnMissileAdded -= OnMissileAdded;
            MyAPIGateway.Missiles.OnMissileRemoved -= OnMissileRemoved;
        }

        private void OnMissileAdded(IMyMissile missile)
        {
            MyAPIGateway.Utilities.ShowMessage("Missile added");
            PhoenixMissileDef missileDef;
            if (MissileStats.TryGetValue(missile.AmmoDefinition.Id.SubtypeName, out missileDef) && missileDef != null)
            {
                MyAPIGateway.Utilities.ShowMessage("Guided missile found");
                missile.Synchronized = true;
                missileGuiders.Add(missile.EntityId, new MissileGuider(missile, missileDef));
            }
        }

        private void OnMissileRemoved(IMyMissile missile)
        {
            if (missileGuiders.ContainsKey(missile.EntityId))
            {
                MyAPIGateway.Utilities.ShowMessage("Guided missile removed");
                missileGuiders.Remove(missile.EntityId);
            }
                
        }

        public override void UpdateBeforeSimulation()
        {
            foreach (var missileGuider in missileGuiders)
            {
                missileGuider.Value.Update();
            }
        }

    } //End public class Torp_Tracking : MySessionComponentBase
}
