import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";

export function SetupWizardSelfSignedCertificateStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { selfSignedCertificateStep } = useWatch({ control });

    return <h1>Self-signed certificate</h1>;
}

export function SetupWizardSelfSignedCertificateStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Domain");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
