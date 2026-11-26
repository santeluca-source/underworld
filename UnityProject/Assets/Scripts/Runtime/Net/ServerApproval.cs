using System.Collections.Generic;
using UnityEngine;

namespace Underworld.Net
{
    /// <summary>
    /// Punto di estensione dell'approvazione: i moduli del server aggiungono qui i loro controlli
    /// (Fase 5: hash del token, lucchetto di sessione). L'approvazione li chiama in ordine di registrazione.
    /// Solo server.
    /// </summary>
    public static class ServerApproval
    {
        static readonly List<IConnectionValidator> ValidatorList = new List<IConnectionValidator>();

        public static IReadOnlyList<IConnectionValidator> Validators => ValidatorList;

        public static void AddValidator(IConnectionValidator validator)
        {
            if (validator != null && !ValidatorList.Contains(validator)) ValidatorList.Add(validator);
        }

        public static void RemoveValidator(IConnectionValidator validator) => ValidatorList.Remove(validator);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ValidatorList.Clear();
    }
}
