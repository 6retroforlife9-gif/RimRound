using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RimRound.AI
{
    internal class Verb_ShootLimitedUse : Verb_Shoot
    {
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref numberOfShotsFired, "rrNumberOfShotsFired", 0);
        }

        protected override bool TryCastShot()
        {
            if (base.TryCastShot())
            {
                numberOfShotsFired++;

                if (this.numberOfShotsFired >= this.numberOfShotsAbleToFire)
                {
                    this.SelfConsume();
                }
                return true;
            }
            if (this.numberOfShotsFired >= this.numberOfShotsAbleToFire)
            {
                this.SelfConsume();
            }

            return false;
        }

        public override void Notify_EquipmentLost()
        {
            base.Notify_EquipmentLost();
            if (this.numberOfShotsFired >= this.numberOfShotsAbleToFire)
            {
                this.SelfConsume();
            }
        }

        private void SelfConsume()
        {
            if (base.EquipmentSource != null && !base.EquipmentSource.Destroyed)
            {
                base.EquipmentSource.Destroy(DestroyMode.Vanish);
            }
        }

        private int numberOfShotsAbleToFire = 6;
        private int numberOfShotsFired = 0;
    }
}
