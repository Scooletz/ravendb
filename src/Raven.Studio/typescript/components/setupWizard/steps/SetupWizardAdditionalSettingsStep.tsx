import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";

export function SetupWizardAdditionalSettingsStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { additionalSettingsStep } = useWatch({ control });

    return <h1>Additional settings</h1>;
}

export function SetupWizardAdditionalSettingsStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Summary");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
