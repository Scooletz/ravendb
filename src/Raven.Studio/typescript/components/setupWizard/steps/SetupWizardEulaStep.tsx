import Code from "components/common/Code";
import { useServices } from "components/hooks/useServices";
import { useAsync } from "react-async-hook";

export default function SetupWizardEulaStep() {
    const { setupWizardService } = useServices();

    const asyncGetEula = useAsync(setupWizardService.getEula, []);

    // TODO enable Continue button when EULA is scrolled to the bottom

    return (
        <div>
            <h2>Read the EULA (End-User License Agreement)</h2>
            <p>The following license agreement must be accepted in order to use this software.</p>
            <Code language="plaintext" code={asyncGetEula.result ?? "Loading"} />
        </div>
    );
}
