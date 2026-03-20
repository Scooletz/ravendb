import React from "react";
import { AboutViewFloating, AccordionItemWrapper } from "components/common/AboutView";

export default function TasksErrorsAboutView() {
    return (
        <AboutViewFloating>
            <AccordionItemWrapper icon="about" color="info" heading="Tasks Errors" description="View tasks errors">
                <div>todo</div>
            </AccordionItemWrapper>
        </AboutViewFloating>
    );
}
