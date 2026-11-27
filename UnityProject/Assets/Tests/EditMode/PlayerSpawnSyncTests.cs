using NUnit.Framework;
using Underworld.Player;
using UnityEngine;

namespace Underworld.Tests
{
    /// <summary>
    /// Regressione del blocco in (0,0,0) di bot_wander: Netcode crea il personaggio nel punto del prefab
    /// e poi sposta il transform. Con Physics.autoSyncTransforms spento la capsula del CharacterController
    /// resta indietro; ThirdPersonController.SyncCharacterController la rimette sul transform.
    /// </summary>
    public sealed class PlayerSpawnSyncTests
    {
        GameObject _go;
        bool _oldAutoSync;

        [SetUp]
        public void SetUp()
        {
            _oldAutoSync = Physics.autoSyncTransforms;
            Physics.autoSyncTransforms = false; // come nel progetto
            _go = new GameObject("CcSyncTest");
            _go.AddComponent<CharacterController>();
        }

        [TearDown]
        public void TearDown()
        {
            Physics.autoSyncTransforms = _oldAutoSync;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void SyncedController_MovesFromNewTransformPosition()
        {
            var cc = _go.GetComponent<CharacterController>();
            var spawn = new Vector3(5.66f, 0.05f, -5.66f);
            _go.transform.position = spawn; // come NetworkSpawnManager dopo Instantiate

            ThirdPersonController.SyncCharacterController(cc);
            cc.Move(new Vector3(0.1f, 0f, 0f));

            Assert.Less(Vector3.Distance(_go.transform.position, spawn + new Vector3(0.1f, 0f, 0f)), 0.05f,
                $"posizione dopo Move: {_go.transform.position}");
        }

        [Test]
        public void UnsyncedController_JumpsBackToOldCapsulePosition()
        {
            // Documenta la causa: senza SyncCharacterController il Move parte dalla capsula vecchia (0,0,0).
            // Se un giorno Unity cambia comportamento, questo test lo dice (e la correzione resta innocua).
            var cc = _go.GetComponent<CharacterController>();
            _go.transform.position = new Vector3(5.66f, 0.05f, -5.66f);
            cc.Move(new Vector3(0.1f, 0f, 0f));
            Assert.Less(Vector3.Distance(_go.transform.position, new Vector3(0.1f, 0f, 0f)), 0.05f,
                $"posizione dopo Move: {_go.transform.position}");
        }

        [Test]
        public void SyncCharacterController_LeavesDisabledControllerOff()
        {
            var cc = _go.GetComponent<CharacterController>();
            cc.enabled = false;
            ThirdPersonController.SyncCharacterController(cc);
            Assert.IsFalse(cc.enabled);
            Assert.DoesNotThrow(() => ThirdPersonController.SyncCharacterController(null));
        }
    }
}
