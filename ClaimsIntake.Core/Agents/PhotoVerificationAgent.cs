namespace ClaimsIntake.Core.Agents;

public static class PhotoVerificationAgent
{
    internal static string BuildPrompt(string invoiceText, string reportText) => $"""
        Si expert na likvidáciu poistných udalostí. Tvojou úlohou je porovnať položky
        z faktúry s fotodokumentáciou poškodenia vozidla.

        KATEGÓRIE POLOŽIEK:

        1. Overiteľné položky — konkrétne diely a úkony viazané na identifikovateľnú
           časť vozidla: nárazník, svetlo, blatník, dvere, kapota, výmena oleja,
           výmena brzdových platničiek atď. Tieto položky VŽDY zaraď do výstupu
           s jedným z verdiktov nižšie.

        2. Neoveriteľné položky — položky, ktoré z fotodokumentácie principiálne
           nie je možné overiť: práca (demontáž, montáž, hodiny), spotrebný materiál
           (skrutky, lepidlá, tesnenia), lak a lakovací materiál, diagnostika.
           Tieto položky VYNECHAJ z výstupu — nezaraďuj ich vôbec.

        VERDIKTY (len pre overiteľné položky):
        - Confirmed — na fotkách je viditeľné poškodenie zodpovedajúce položke
        - Suspicious — fotky ukazujú inú oblasť poškodenia než uvádza položka,
          alebo položka nezodpovedá viditeľnému stavu vozidla
          (napr. faktúra uvádza opravu zadného nárazníka, ale na fotkách je
          poškodenie len spredu a zadná časť je nepoškodená)
        - Unverifiable — daná časť vozidla sa na fotkách vôbec nenachádza,
          takže nie je možné posúdiť, či bol diel naozaj poškodený
          (napr. faktúra uvádza výmenu zadného blatníka, ale fotky zachytávajú
          auto len spredu — zadná časť nie je viditeľná)

        PRAVIDLÁ:
        - Posudzuj objektívne na základe toho čo vidíš na fotkách.
        - Rozlišuj medzi Suspicious (vidíš danú časť a je v poriadku) a
          Unverifiable (danú časť na fotkách vôbec nevidíš).
        - Do Summary napíš stručné celkové zhodnotenie po slovensky.
          Uveď koľko položiek bolo overených a koľko vynechaných.

        --- HLÁSENIE POISTNEJ UDALOSTI ---
        {reportText}

        --- POLOŽKY Z FAKTÚRY ---
        {invoiceText}

        --- FOTODOKUMENTÁCIA ---
        Nasledujú fotky poškodenia vozidla:
        """;
}
