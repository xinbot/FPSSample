using Unity.Entities;
using UnityEngine;

public class AutomaticRifleUI : AbilityUI
{
    private int _ammoInClip = -1;
    private int _clipSize = -1;

    [SerializeField] private TMPro.TextMeshProUGUI m_AmmoInClipText;
    [SerializeField] private TMPro.TextMeshProUGUI m_ClipSizeText;

    public override void UpdateAbilityUI(EntityManager entityManager, ref GameTime time)
    {
        var charRepAll = entityManager.GetComponentData<CharacterReplicatedData>(abilityOwner);
        var ability = charRepAll.FindAbilityWithComponent(entityManager, typeof(Ability_AutoRifle.PredictedState));
        GameDebug.Assert(ability != Entity.Null, "AbilityController does not own a Ability_AutoRifle ability");

        var state = entityManager.GetComponentData<Ability_AutoRifle.PredictedState>(ability);
        if (_ammoInClip != state.ammoInClip)
        {
            _ammoInClip = state.ammoInClip;
            m_AmmoInClipText.text = _ammoInClip.ToString();
        }

        var settings = entityManager.GetComponentData<Ability_AutoRifle.Settings>(ability);
        if (_clipSize != settings.clipSize)
        {
            _clipSize = settings.clipSize;
            m_ClipSizeText.text = "/ " + _clipSize;
        }
    }
}