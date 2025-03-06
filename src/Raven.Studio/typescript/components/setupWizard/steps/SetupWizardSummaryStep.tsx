import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";

export function SetupWizardSummaryStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const formData = useWatch({ control });

    return <h1>Summary</h1>;
}

export function SetupWizardSummaryStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Finish");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Finish <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
