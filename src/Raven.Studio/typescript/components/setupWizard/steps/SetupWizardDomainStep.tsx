import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import { FormGroup, FormLabel, FormInput } from "components/common/Form";

export function SetupWizardDomainStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { domainStep } = useWatch({ control });

    // TODO What is this? tooltips

    // TODO
    // Jeśli użytkownik ma więcej niż jedną domenę podpiętą pod licencje to zamiast inputu ma creatable select.
    // W przypadku gdy użytkownik wybierze domenę, która jest już w użyciu powinien pojawić się alert o rekordach DNS.

    const isDomainAlreadyInUse = true; // TODO

    return (
        <div>
            <h2>Domain</h2>
            <p>
                Enter your own domain to be linked with our default hosting zone, where your database will be hosted and
                protected with a security certificate.
            </p>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Domain</div>
                    <div className="text-info">
                        <Icon icon="info" /> What is this?
                    </div>
                </FormLabel>
                <FormInput
                    type="text"
                    control={control}
                    name="domainStep.domain"
                    placeholder="Enter chosen subdomain"
                    addon=".development.run"
                />
            </FormGroup>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Email</div>
                    <div className="text-info">
                        <Icon icon="info" /> What is this?
                    </div>
                </FormLabel>
                <FormInput type="text" control={control} name="domainStep.email" disabled />
            </FormGroup>
        </div>
    );
}

export function SetupWizardDomainStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Node address");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
