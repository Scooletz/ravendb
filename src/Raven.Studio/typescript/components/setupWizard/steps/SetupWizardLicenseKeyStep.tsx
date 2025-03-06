import { useFormContext } from "react-hook-form";
import { useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import { Button } from "react-bootstrap";

export function SetupWizardLicenseKeyStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { licenseKeyStep } = useWatch({ control });

    return <h1>License key</h1>;
}

export function SetupWizardLicenseKeyStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleBack = () => {
        setValue("currentStep", "Setup method");
    };

    const handleContinue = () => {
        setValue("currentStep", "Security");
    };

    return (
        <div className="d-flex justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
