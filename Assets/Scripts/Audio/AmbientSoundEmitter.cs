using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ClientOnlyComponent]
public class AmbientSoundEmitter : MonoBehaviour
{
    public SoundDef sound;

    SoundSystem.SoundHandle handle;

    void Start()
    {
        // TODO (petera) remove if once we get null soundsystem. Headless check needed
        // for headless clients
        if(!Game.IsHeadless())
            handle = Game.soundSystem.Play(sound);
    }

    void OnDisable()
    {
        if (!Game.IsHeadless())
            Game.soundSystem.Stop(handle, 4.0f);
    }
}
