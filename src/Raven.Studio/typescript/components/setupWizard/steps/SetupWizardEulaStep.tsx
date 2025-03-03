import Code from "components/common/Code";
import { useServices } from "components/hooks/useServices";
import { useAsync } from "react-async-hook";

export default function SetupWizardEulaStep() {
    const { setupWizardService } = useServices();

    const asyncGetEula = useAsync(setupWizardService.getEula, []);

    return (
        <div>
            <h1>Read the EULA (End-User License Agreement)</h1>
            <p>The following license agreement must be accepted in order to use this software.</p>
            <div style={{ height: 300, overflow: "auto" }}>
                <Code language="plaintext" code={asyncGetEula.result ?? "Loading"} />
            </div>
        </div>
    );
}
