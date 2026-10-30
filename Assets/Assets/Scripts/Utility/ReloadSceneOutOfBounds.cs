using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public class ReloadSceneOutOfBounds : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Out Of Bounds"))
        {
#if DUMP_WORLDGEN
            Debug.Log("WorldGenOutOfBounds " + name + " at " + transform.position + " hit " + other.name + " at " + other.transform.position);
#endif
            if (CoopSession.InGame && CoopPlayers.Instance != null)
            {
                OutOfBoundsInCoop();
                return;
            }
            SceneManager.LoadScene(1);
        }
    }

    // can't just reload in coop, worlds would split
    private void OutOfBoundsInCoop()
    {
        CoopPlayers instance = CoopPlayers.Instance;
        if (transform == instance.Remote)
        {
            return;
        }
        if (transform == instance.Local)
        {
            // on the ket the ket's fall handles it, otherwise respawn by the partner
            if (CoopKettenkrad.Instance == null || CoopKettenkrad.Instance.LocalSeat == CoopKettenkrad.SeatState.Off)
            {
                instance.RescueLocalPlayer();
            }
            return;
        }
        // our ket fell out: host restarts the level for both
        if (CoopKettenkrad.FollowsRemote)
        {
            return;
        }
        CoopSession.Instance.RestartLevel();
    }
}
