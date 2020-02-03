using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignNow.Net;
using SignNow.Net.Internal.Extensions;
using SignNow.Net.Model;
using SignNow.Net.Test;
using SignNow.Net.Test.Constants;
using SignNow.Net.Test.Extensions;

namespace FeatureTests
{
    [TestClass]
    public class FreeFormInviteTest : AuthorizedApiTestBase
    {
        [TestMethod]
        public void DocumentOwnerCanSendFreeFormInviteToUser()
        {
            // Init all required data: User, Document, Invite
            var signNow = new SignNowContext(Token);

            var invitee = new User
            {
                Email = "signnow.tutorial+test@signnow.com",
                FirstName = "Alex",
                LastName = "Dou",
                Active = true
            };
            var invite = new FreeFormSignInvite(invitee.Email);

            DocumentId = UploadTestDocument(PdfFilePath, signNow.Documents);

            // Creating Invite request
            var inviteResponseTask = signNow.Invites.CreateInviteAsync(DocumentId, invite);
            Task.WaitAll(inviteResponseTask);

            var inviteResponse = inviteResponseTask.Result;

            Assert.IsFalse(inviteResponseTask.IsFaulted, "Invite request should be created.");
            Assert.AreEqual(invite.Recipient, invitee.Email, "Freeform invite request should contains proper user email.");
            Assert.AreEqual(inviteResponse.Id, inviteResponse.Id.ValidateId(), "Successful invite response should contains valid Invite ID.");

            // Check if invite was successful and the document contains invite request data
            var documentInfo = signNow.Documents.GetDocumentAsync(DocumentId).Result;
            var inviteIdx = documentInfo.InviteRequests.FindIndex(request => request.Id == inviteResponse.Id);
            var documentInviteRequest = documentInfo.InviteRequests[inviteIdx];

            Assert.AreEqual(DocumentId, documentInfo.Id, "You should get proper document details.");
            Assert.AreEqual(inviteResponse.Id, documentInviteRequest.Id, "Document should contains freeform invite ID after invite has been sent.");
            Assert.AreEqual(invitee.Email, documentInviteRequest.Signer, "Invite should contains user email whom was sent invite request.");
            Assert.IsNull(documentInviteRequest.IsCanceled, "Invite status should not be canceled by default.");
            Assert.AreEqual(SignStatus.Pending, documentInviteRequest.Status);
            Assert.AreEqual(SignStatus.Pending, documentInfo.Status);
        }

        [TestMethod]
        public void ShouldGetDocumentSignedStatusForFreeFormInvite()
        {
            var mockDocument = JsonFixtures.DocumentTemplate.AsJsonObject();

            Assert.AreEqual(SignStatus.None, new SignNowDocument().Status);

            // successful freeform invite should have invite request inside the document
            var inviteRequests = (JArray)mockDocument["requests"];
            // add freeform invite for single signer to document from json template
            inviteRequests.Add(JsonFixtures.FreeFormInviteTemplate.AsJsonObject());
            inviteRequests[0]["signature_id"] = null;

            var documentWithRequest = JsonConvert.DeserializeObject<SignNowDocument>(mockDocument.ToString());

            // Freeform invite created assertions
            Assert.AreEqual(1, documentWithRequest.InviteRequests.Count);
            Assert.AreEqual("test.user@signnow.com", documentWithRequest.InviteRequests[0].Owner);
            Assert.AreEqual("signer@signnow.com", documentWithRequest.InviteRequests[0].Signer);
            Assert.IsNull(documentWithRequest.InviteRequests[0].SignatureId);
            Assert.AreEqual(SignStatus.Pending, documentWithRequest.InviteRequests[0].Status);
            Assert.AreEqual(SignStatus.Pending, documentWithRequest.Status);

            // Add signature by signing the document
            var signatures = (JArray)mockDocument["signatures"];
            inviteRequests[0]["signature_id"] = "signatureId00000000000000000000000SIGNED";
            signatures.Add(JsonFixtures.SignatureTemplate.AsJsonObject());
            signatures[0]["id"] = inviteRequests[0]["signature_id"];
            signatures[0]["signature_request_id"] = inviteRequests[0]["unique_id"];

            var documentSigned = JsonConvert.DeserializeObject<SignNowDocument>(mockDocument.ToString());
            var actualSignature = documentSigned.Signatures[0];
            var actualInvite = documentSigned.InviteRequests[0];

            // asserts for document signed with only one freeform invite
            Assert.AreEqual(1, documentSigned.InviteRequests.Count);
            Assert.AreEqual(1, documentSigned.Signatures.Count);
            Assert.AreEqual(actualSignature.Id, actualInvite.SignatureId);
            Assert.AreEqual(actualSignature.UserId, actualInvite.UserId);
            Assert.AreEqual(actualSignature.SignatureRequestId, actualInvite.Id);
            Assert.AreEqual(actualSignature.Email, actualInvite.Signer);
            Assert.AreEqual(SignStatus.Completed, documentSigned.InviteRequests[0].Status);
            Assert.AreEqual(SignStatus.Completed, documentSigned.Status);

            // add second freeform invite to the document
            inviteRequests.Add(
                JsonConvert.DeserializeObject(@"{
                    'unique_id': 'freeformInviteId000000000000000000000001',
                    'id': 'freeformInviteId000000000000000000000001',
                    'user_id': 'userId0000000000000000000000000000000001',
                    'created': '1579090178',
                    'originator_email': 'test.user@signnow.com',
                    'signer_email': 'signer1@signnow.com',
                    'canceled': null,
                    'redirect_uri': null
                }")
            );

            var documentWithTwoRequests = JsonConvert.DeserializeObject<SignNowDocument>(mockDocument.ToString());

            // asserts for document with two freeform invites (one - signed, second - not signed yet)
            Assert.AreEqual(2, documentWithTwoRequests.InviteRequests.Count);
            Assert.AreEqual(1, documentWithTwoRequests.Signatures.Count);
            Assert.IsTrue(documentWithTwoRequests.InviteRequests.TrueForAll(itm => itm.Owner == "test.user@signnow.com"));
            Assert.IsNotNull(documentWithTwoRequests.InviteRequests[0].SignatureId);
            Assert.IsNull(documentWithTwoRequests.InviteRequests[1].SignatureId);
            Assert.AreEqual(SignStatus.Completed, documentWithTwoRequests.InviteRequests[0].Status);
            Assert.AreEqual(SignStatus.Pending, documentWithTwoRequests.InviteRequests[1].Status);
            Assert.AreEqual(SignStatus.Pending, documentWithTwoRequests.Status);

            // sign second freeform invite and complete the document signing
            signatures.Add(JsonFixtures.SignatureTemplate.AsJsonObject());
            inviteRequests[1]["signature_id"] = "signatureId10000000000000000000000SIGNED";
            signatures[1]["id"] = inviteRequests[1]["signature_id"];
            signatures[1]["signature_request_id"] = inviteRequests[1]["unique_id"];

            var documentWithTwoRequestsSigned = JsonConvert.DeserializeObject<SignNowDocument>(mockDocument.ToString());

            // check if document fullfilled
            Assert.AreEqual(2, documentWithTwoRequestsSigned.Signatures.Count);
            Assert.IsTrue(documentWithTwoRequestsSigned.InviteRequests.TrueForAll(req => req.Status == SignStatus.Completed));
            Assert.AreEqual(SignStatus.Completed, documentWithTwoRequestsSigned.Status);
        }
    }
}
