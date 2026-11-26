using System;
using System.Collections;

namespace Underworld.Testing
{
    /// <summary>
    /// Segna una classe come scenario di test automatico. Il nome è quello che si passa a -autotest.
    /// Convenzione: "&lt;scenario&gt;.&lt;ruolo&gt;" (es. "connect_two_clients.walker"), così il file
    /// Tools/scenarios/&lt;scenario&gt;.json dice quale ruolo gira in ogni processo.
    /// La classe deve implementare IAutoTestScenario e avere un costruttore vuoto.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoTestScenarioAttribute : Attribute
    {
        public string Name { get; }
        public AutoTestScenarioAttribute(string name) => Name = name;
    }

    /// <summary>
    /// Uno scenario di test automatico (contratto, sezione 12). Gira dentro il gioco vero (client o server)
    /// come coroutine: può aspettare (ctx.WaitUntil, ctx.Wait, yield return null) e controllare (ctx.Check).
    /// Lo scenario passa se tutti i controlli passano, ce n'è almeno uno e non ci sono errori o tempo scaduto.
    /// </summary>
    public interface IAutoTestScenario
    {
        IEnumerator Run(AutoTestContext ctx);
    }
}
