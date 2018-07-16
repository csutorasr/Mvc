﻿using System;

namespace Microsoft.AspNetCore.Mvc.Analyzers.ExtractToNewConvention_AddsNewTypeAndConventionMethod._OUTPUT_
{
    [ApiController]
    [ApiConventionType(typeof(TestProjectConventions))]
    public class TestController : ControllerBase
    {
        public IActionResult EditPet(int petId, PetModel pet)
        {
            if (petId <= 0)
            {
                return NotFound();
            }

            UpdatePet(pet);
            return Accepted();
        }

        private void UpdatePet(PetModel pet)
        {
            throw new NotImplementedException();
        }
    }

    public class PetModel { }
}
