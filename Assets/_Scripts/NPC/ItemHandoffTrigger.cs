using System;
using UnityEngine;

namespace Core3lb
{
    public class ItemHandoffTrigger : ItemIDTrigger_PassFail
    {
        public GameObject recipient;
        public ItemHandoff LastReceipt { get; private set; }
        public event Action<ItemHandoff> Transferred;

        public override void TriggerEnterSuccess(Collider collision)
        {
            var item = collision.GetComponent<ItemID>();
            var grab = collision.GetComponent<BaseGrabObject>();
            if (!item || !grab || !grab.isGrabbed || !grab.currentHand ||
                !collision.gameObject.activeInHierarchy) return;
            var giver = grab.currentHand.gameObject;
            string name = item.DisplayName, id = item.itemID, group = item.groupID;
            base.TriggerEnterSuccess(collision);
            if (grab.isGrabbed || item.gameObject.activeSelf) return;
            LastReceipt = new ItemHandoff(Guid.NewGuid().ToString("N"), id, group, name, giver, recipient ? recipient : gameObject);
            Transferred?.Invoke(LastReceipt);
        }
    }

    public sealed class ItemHandoff
    {
        public readonly string eventId, itemId, groupId, itemName;
        public readonly GameObject giver, recipient;
        public ItemHandoff(string eventId, string itemId, string groupId, string itemName, GameObject giver, GameObject recipient)
        {
            this.eventId = eventId; this.itemId = itemId; this.groupId = groupId; this.itemName = itemName;
            this.giver = giver; this.recipient = recipient;
        }
    }
}
