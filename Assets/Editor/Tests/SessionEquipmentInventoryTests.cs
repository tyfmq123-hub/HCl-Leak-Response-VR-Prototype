using HClTraining.SharedXR;
using NUnit.Framework;
using UnityEngine;

public sealed class SessionEquipmentInventoryTests
{
    GameObject host;
    SessionEquipmentInventory inventory;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("Session Inventory Test");
        inventory = host.AddComponent<SessionEquipmentInventory>();
        inventory.BeginNewSession();
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null)
            Object.DestroyImmediate(host);
    }

    [Test]
    public void CheckOutReturnAndConsume_KeepCountsConsistent()
    {
        Assert.That(inventory.Store(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.GetStoredCount(SharedEquipmentIds.GasDetector), Is.EqualTo(1));

        Assert.That(inventory.TryCheckOut(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.GetStoredCount(SharedEquipmentIds.GasDetector), Is.Zero);
        Assert.That(inventory.GetCheckedOutCount(SharedEquipmentIds.GasDetector), Is.EqualTo(1));

        Assert.That(inventory.ReturnCheckedOut(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.GetStoredCount(SharedEquipmentIds.GasDetector), Is.EqualTo(1));
        Assert.That(inventory.TryCheckOut(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.ConsumeCheckedOut(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.Contains(SharedEquipmentIds.GasDetector), Is.False);
    }

    [Test]
    public void StandaloneSeed_DoesNotOverwriteResolvedPpeResult()
    {
        inventory.ReplaceFromPpe(new[] { SharedEquipmentIds.GasDetector });

        Assert.That(
            inventory.SeedStandalone(SessionEquipmentPresets.LeakResponseStandaloneEquipment),
            Is.False);
        Assert.That(inventory.Contains(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.Contains(SharedEquipmentIds.AbsorbentSock), Is.False);
        Assert.That(inventory.Source, Is.EqualTo(SessionInventorySource.PpeResult));
    }

    [Test]
    public void StandaloneSeed_ProvidesOnlyStageDevelopmentItems()
    {
        Assert.That(
            inventory.SeedStandalone(SessionEquipmentPresets.PreEntryStandaloneEquipment),
            Is.True);
        Assert.That(inventory.Contains(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.Contains(SharedEquipmentIds.AbsorbentSock), Is.False);
        Assert.That(inventory.Source, Is.EqualTo(SessionInventorySource.StageStandalonePreset));
    }

    [Test]
    public void ExplicitPpePacking_IsNotOverwrittenByLegacyCompletionBridge()
    {
        inventory.BeginPpeBagCapture();
        inventory.Store(SharedEquipmentIds.GasDetector);

        inventory.ReplaceFromPpe(SessionEquipmentPresets.OfficialFieldEquipment);

        Assert.That(inventory.Contains(SharedEquipmentIds.GasDetector), Is.True);
        Assert.That(inventory.Contains(SharedEquipmentIds.AbsorbentSock), Is.False);
        Assert.That(inventory.HasExplicitPpeStorage, Is.True);
    }
}
