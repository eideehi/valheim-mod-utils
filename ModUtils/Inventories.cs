using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModUtils
{
    public static class Inventories
    {
        public static IEnumerable<ItemDrop.ItemData> GetItems(Inventory inventory, string name,
            int quality = -1, bool isPrefabName = false)
        {
            return inventory.GetAllItems()
                            .Where(data =>
                                (isPrefabName
                                    ? data.m_dropPrefab.name == name
                                    : data.m_shared.m_name == name) &&
                                (quality < 0 || data.m_quality == quality));
        }

        public static int FillFreeStackSpace(Inventory from, Inventory to, string name, int amount,
            int quality = -1, bool isPrefabName = false)
        {
            if (amount <= 0) return 0;

            var remain = Mathf.Min(amount, to.FindFreeStackSpace(name));
            if (remain == 0) return 0;

            var fillCount = 0;
            var toInventoryItems = GetItems(to, name, quality, isPrefabName).ToList();
            foreach (var fromInventoryItem in GetItems(from, name, quality, isPrefabName))
            foreach (var toInventoryItem in toInventoryItems)
            {
                if (toInventoryItem.m_stack >= toInventoryItem.m_shared.m_maxStackSize)
                    continue;

                var count = Mathf.Min(Mathf.Min(remain, fromInventoryItem.m_stack),
                    toInventoryItem.m_shared.m_maxStackSize - toInventoryItem.m_stack);
                if (count == 0) continue;

                toInventoryItem.m_stack += count;
                fillCount += count;

                fromInventoryItem.m_stack -= count;
                remain -= count;

                if (fromInventoryItem.m_stack == 0)
                    from.RemoveItem(fromInventoryItem);
                else
                    Reflections.InvokeMethod(from, "Changed");

                Reflections.InvokeMethod(to, "Changed");

                if (remain == 0) goto LOOP_EXIT;
                if (fromInventoryItem.m_stack == 0) break;
            }

        LOOP_EXIT: ;
            return fillCount;
        }

        public static int RemoveItem(Inventory inventory, string name, int amount, int quality = -1,
            bool isPrefabName = false)
        {
            if (amount <= 0) return 0;

            var removeCount = 0;
            foreach (var item in GetItems(inventory, name, quality, isPrefabName))
            {
                var count = Mathf.Min(amount, item.m_stack);
                item.m_stack -= count;

                removeCount += count;
                amount -= count;

                if (item.m_stack == 0)
                    inventory.RemoveItem(item);
                else
                    Reflections.InvokeMethod(inventory, "Changed");

                if (amount == 0) break;
            }

            return removeCount;
        }
    }
}