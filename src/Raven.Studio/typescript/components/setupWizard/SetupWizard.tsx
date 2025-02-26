import { Button } from "reactstrap";
import { Icon } from "components/common/Icon";
import Code from "components/common/Code";
import { useAsync } from "react-async-hook";
import { useServices } from "components/hooks/useServices";

const logo = require("Content/img/ravendb_logo.svg");

export default function SetupWizard() {
    const { setupWizardService } = useServices();

    const asyncGetEula = useAsync(setupWizardService.getEula, []);

    return (
        <div className="hstack" style={{ height: 1000 }}>
            <div className="vstack flex-grow-1">
                <div>
                    <img src={logo} alt="RavenDB Logo" width="120" />
                </div>
                <div className="flex-grow-1">
                    <h1>Read the EULA (End-User License Agreement)</h1>
                    <p>The following license agreement must be accepted in order to use this software.</p>
                    <div style={{ height: 300, overflow: "auto" }}>
                        <Code language="plaintext" code={asyncGetEula.result ?? "Loading"} />
                    </div>
                </div>
                <div>
                    <hr />
                    <Button color="primary" className="rounded-pill">
                        Continue <Icon icon="arrow-right" />
                    </Button>
                </div>
            </div>
            <div className="w-25" style={{ backgroundColor: "red" }}></div>
        </div>
    );
}
